using Fiscal.Application.Armazenamento;
using Fiscal.Application.Comum;
using Fiscal.Application.Documentos;
using Fiscal.Application.Processamento;
using Fiscal.Application.Resumos;
using Fiscal.Domain.Comum;
using Fiscal.Domain.Documentos;
using Microsoft.Extensions.Logging;

namespace Fiscal.Application.Lotes;

public enum ResultadoDaIngestao
{
    Ingerido,
    Duplicado,
    Rejeitado,

    /// <summary>Reentrega: o inbox reconheceu que esta mensagem já teve efeito.</summary>
    JaProcessado,
}

/// <summary>
/// O trabalho assíncrono de verdade: baixa o XML, valida, persiste o documento e
/// avança a máquina de estados do item e do lote.
/// <para>
/// A leitura do storage e o parse ficam FORA da transação, de propósito. São
/// chamadas de rede e CPU que não tocam o banco; mantê-las dentro seguraria uma
/// transação aberta por todo o tempo de download, e transação aberta segura locks.
/// Quando a transação abre, já se sabe o que vai acontecer — falta só aplicar.
/// </para>
/// <para>
/// Dentro da transação caem cinco coisas que precisam ser indivisíveis: o inbox da
/// mensagem, o documento, a situação do item, a situação do lote e o resumo do
/// emitente. Se o processo morrer no meio, o rollback desfaz todas, e a reentrega
/// encontra o mundo como estava.
/// </para>
/// </summary>
public sealed class IngerirArquivo(
    IArmazenamentoDeXml armazenamento,
    ISeletorDeParser seletor,
    IRepositorioDocumentos documentos,
    IRepositorioLotes lotes,
    IRepositorioResumos resumos,
    IRepositorioProcessamento processamento,
    IUnidadeDeTrabalho unidade,
    TimeProvider relogio,
    ILogger<IngerirArquivo> logger)
{
    public const string NomeDoConsumidor = "ingestao-de-arquivo";

    public async Task<ResultadoDaIngestao> ExecutarAsync(
        ArquivoRecebido mensagem,
        CancellationToken cancellationToken)
    {
        var (lido, motivoDeRejeicao) = await InterpretarAsync(mensagem, cancellationToken);

        return await unidade.ExecutarAsync(
            token => AplicarAsync(mensagem, lido, motivoDeRejeicao, token),
            cancellationToken);
    }

    /// <summary>
    /// Baixa e interpreta. Devolve ou o documento lido, ou o motivo da rejeição —
    /// nunca os dois. Falha de leitura vira motivo, não exceção: XML inválido é
    /// resultado legítimo de uma ingestão, e o cliente precisa vê-lo no lote.
    /// </summary>
    private async Task<(DocumentoFiscalLido? Lido, string? Motivo)> InterpretarAsync(
        ArquivoRecebido mensagem,
        CancellationToken cancellationToken)
    {
        var xml = await armazenamento.LerAsync(mensagem.ChaveDeArmazenamento, cancellationToken);

        if (xml is null)
        {
            return (null, "Arquivo não encontrado no armazenamento.");
        }

        DocumentoFiscalLido lido;

        try
        {
            lido = seletor.Selecionar(xml).Ler(xml);
        }
        catch (DomainException excecao)
        {
            return (null, excecao.Message);
        }

        // O emitente do XML tem de ser o dono do lote. Sem esta checagem, um
        // contribuinte alimentaria a base de outro — e o filtro global depois
        // esconderia o registro do próprio dono.
        if (lido.CnpjEmitente != mensagem.CnpjProprietario)
        {
            return (null, "O CNPJ emitente do XML não corresponde ao CNPJ que enviou o lote.");
        }

        return (lido, null);
    }

    private async Task<ResultadoDaIngestao> AplicarAsync(
        ArquivoRecebido mensagem,
        DocumentoFiscalLido? lido,
        string? motivoDeRejeicao,
        CancellationToken cancellationToken)
    {
        var agora = relogio.GetUtcNow();

        // Primeira coisa dentro da transação. Se já houver registro, nada mais roda.
        var primeiraVez = await processamento.TentarRegistrarAsync(
            mensagem.ItemId.ToString(), NomeDoConsumidor, agora, cancellationToken);

        if (!primeiraVez)
        {
            logger.LogInformation("Item {ItemId} já processado; reentrega ignorada.", mensagem.ItemId);

            return ResultadoDaIngestao.JaProcessado;
        }

        var lote = await lotes.ObterParaEdicaoAsync(mensagem.LoteId, cancellationToken)
            ?? throw new DomainException($"Lote {mensagem.LoteId} não encontrado.");

        var item = lote.Itens.SingleOrDefault(i => i.Id == mensagem.ItemId)
            ?? throw new DomainException($"Item {mensagem.ItemId} não pertence ao lote {mensagem.LoteId}.");

        var resultado = motivoDeRejeicao is not null
            ? Rejeitar(item, motivoDeRejeicao, agora)
            : await PersistirAsync(item, lido!, mensagem.ChaveDeArmazenamento, agora, cancellationToken);

        lote.Recalcular(agora);

        await lotes.SalvarAsync(cancellationToken);

        return resultado;
    }

    private static ResultadoDaIngestao Rejeitar(Domain.Lotes.ItemDoLote item, string motivo, DateTimeOffset agora)
    {
        item.MarcarRejeitado(motivo, agora);

        return ResultadoDaIngestao.Rejeitado;
    }

    private async Task<ResultadoDaIngestao> PersistirAsync(
        Domain.Lotes.ItemDoLote item,
        DocumentoFiscalLido lido,
        string hash,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        var documento = DocumentoFiscal.Registrar(
            lido.Tipo, lido.ChaveAcesso, lido.Numero, lido.Serie,
            lido.CnpjEmitente, lido.NomeEmitente, lido.UfEmitente,
            lido.DocumentoDestinatario, lido.NomeDestinatario,
            lido.DataEmissao, lido.ValorTotal, hash,
            lido.Itens.Select(i => ItemDocumentoFiscal.Criar(
                i.Numero, i.Codigo, i.Descricao, i.Ncm, i.Cfop, i.Quantidade, i.ValorUnitario, i.ValorTotal)),
            agora);

        // Uma escrita, sem consulta prévia: o índice único decide. Ver a nota sobre
        // savepoint em Escrita.TentarAsync — sem ele, a violação abortaria toda a
        // transação da ingestão, não só esta tentativa.
        if (await documentos.TentarInserirAsync(documento, cancellationToken))
        {
            item.MarcarIngerido(documento.Id, agora);

            await resumos.AcumularAsync(
                documento.CnpjEmitente,
                $"{documento.DataEmissao.Year:D4}-{documento.DataEmissao.Month:D2}",
                documento.ValorTotal,
                agora,
                cancellationToken);

            return ResultadoDaIngestao.Ingerido;
        }

        var existente = await documentos.ObterPorChaveAsync(lido.Tipo, lido.ChaveAcesso, cancellationToken)
            ?? throw new DomainException(
                "Índice único recusou a chave mas o documento não foi encontrado.");

        if (existente.HashConteudo == hash)
        {
            // Mesmo arquivo já ingerido antes, possivelmente em outro lote. Não é
            // erro: o cliente pediu que o documento existisse, e ele existe.
            item.MarcarDuplicado(existente.Id, agora);

            return ResultadoDaIngestao.Duplicado;
        }

        item.MarcarRejeitado(
            "A chave de acesso já existe com um XML diferente. Documento fiscal autorizado é "
            + "imutável: divergência indica que um dos dois arquivos não é o documento oficial.",
            agora);

        return ResultadoDaIngestao.Rejeitado;
    }
}
