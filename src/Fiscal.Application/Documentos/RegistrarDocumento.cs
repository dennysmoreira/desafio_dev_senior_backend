using Fiscal.Application.Mensageria;
using Fiscal.Application.Seguranca;
using Fiscal.Domain.Comum;
using Fiscal.Domain.Documentos;
using Microsoft.Extensions.Logging;

namespace Fiscal.Application.Documentos;

public enum ResultadoIngestao
{
    /// <summary>Documento novo, gravado agora. A API responde 201.</summary>
    Criado,

    /// <summary>Chave já existia com XML idêntico. A API responde 200 e sinaliza replay.</summary>
    Repetido,

    /// <summary>Chave já existia com XML diferente. A API responde 409 e nada é gravado.</summary>
    Divergente,

    /// <summary>O emitente do XML não é o CNPJ autenticado. A API responde 403.</summary>
    EmitenteNaoAutorizado,
}

/// <summary><c>Documento</c> é nulo apenas quando o resultado é <see cref="ResultadoIngestao.EmitenteNaoAutorizado"/>.</summary>
public sealed record RespostaIngestao(ResultadoIngestao Resultado, DocumentoFiscal? Documento);

public sealed class RegistrarDocumento(
    ISeletorDeParser seletor,
    IRepositorioDocumentos repositorio,
    IPublicadorEventos publicador,
    IContextoAcesso contexto,
    TimeProvider relogio,
    ILogger<RegistrarDocumento> logger)
{
    public async Task<RespostaIngestao> ExecutarAsync(
        ReadOnlyMemory<byte> xml,
        CancellationToken cancellationToken)
    {
        var hash = HashConteudo.Calcular(xml.Span);
        var lido = seletor.Selecionar(xml).Ler(xml);

        // O CNPJ do emitente vem do XML; o CNPJ autorizado vem da autenticação.
        // Aceitar documento de outro emitente permitiria alimentar a base de um
        // terceiro — e o filtro global depois esconderia o registro do próprio dono.
        if (lido.CnpjEmitente != contexto.CnpjAutorizado)
        {
            logger.LogWarning(
                "Ingestão recusada: emitente {Emitente} difere do CNPJ autenticado.",
                DadosSensiveis.Mascarar(lido.CnpjEmitente));

            return new RespostaIngestao(ResultadoIngestao.EmitenteNaoAutorizado, null);
        }

        var agora = relogio.GetUtcNow();

        var documento = DocumentoFiscal.Registrar(
            lido.Tipo,
            lido.ChaveAcesso,
            lido.Numero,
            lido.Serie,
            lido.CnpjEmitente,
            lido.NomeEmitente,
            lido.UfEmitente,
            lido.DocumentoDestinatario,
            lido.NomeDestinatario,
            lido.DataEmissao,
            lido.ValorTotal,
            hash,
            lido.Itens.Select(i => ItemDocumentoFiscal.Criar(
                i.Numero, i.Codigo, i.Descricao, i.Ncm, i.Cfop, i.Quantidade, i.ValorUnitario, i.ValorTotal)),
            agora);

        // Caminho feliz: uma única escrita, sem leitura prévia. A unicidade é
        // garantida pelo índice, não por uma checagem que teria janela de corrida
        // entre o SELECT e o INSERT.
        if (await repositorio.TentarInserirAsync(documento, cancellationToken))
        {
            await publicador.PublicarAsync(
                new DocumentoProcessado(
                    hash,
                    documento.Id,
                    documento.ChaveAcesso,
                    documento.CnpjEmitente,
                    documento.DataEmissao,
                    documento.ValorTotal),
                cancellationToken);

            return new RespostaIngestao(ResultadoIngestao.Criado, documento);
        }

        // Perdeu a corrida ou é reenvio: só aqui vale a pena consultar.
        var existente = await repositorio.ObterPorChaveAsync(lido.Tipo, lido.ChaveAcesso, cancellationToken)
            ?? throw new DomainException(
                "Índice único recusou a chave mas o documento não foi encontrado. " +
                "Ocorre se a chave pertence a outro CNPJ — o filtro global esconde a linha.");

        if (existente.HashConteudo == hash)
        {
            // Não republica o evento: o efeito colateral já ocorreu na primeira vez.
            return new RespostaIngestao(ResultadoIngestao.Repetido, existente);
        }

        logger.LogWarning(
            "Divergência na chave {Chave}: XML recebido difere do já armazenado.",
            existente.ChaveAcesso);

        return new RespostaIngestao(ResultadoIngestao.Divergente, existente);
    }
}
