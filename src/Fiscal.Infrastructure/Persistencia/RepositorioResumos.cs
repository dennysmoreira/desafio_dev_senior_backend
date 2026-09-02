using Fiscal.Application.Resumos;
using Fiscal.Domain.Processamento;
using Fiscal.Domain.Resumos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fiscal.Infrastructure.Persistencia;

public sealed class RepositorioResumos(FiscalDbContext db) : IRepositorioResumos
{
    private const string ViolacaoDeUnicidade = "23505";

    public async Task<bool> AcumularAsync(
        string mensagemId,
        string consumidor,
        string cnpjEmitente,
        string competencia,
        decimal valorDocumento,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        // Inbox e efeito colateral na mesma transação. Se o processo morrer entre os
        // dois, o rollback desfaz ambos e a reentrega funciona — que é exatamente o
        // caso que uma verificação prévia fora de transação não cobre.
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

        db.MensagensProcessadas.Add(MensagemProcessada.Registrar(mensagemId, consumidor, agora));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao)
            when (excecao.InnerException is PostgresException { SqlState: ViolacaoDeUnicidade })
        {
            // Já processada. O banco decidiu, não um SELECT nosso.
            await transacao.RollbackAsync(cancellationToken);

            return false;
        }

        var resumo = await db.Resumos
            .IgnoreQueryFilters([FiltrosDeConsulta.IsolamentoPorCnpj])
            .FirstOrDefaultAsync(
                r => r.CnpjEmitente == cnpjEmitente && r.Competencia == competencia,
                cancellationToken);

        if (resumo is null)
        {
            resumo = ResumoEmitente.Criar(cnpjEmitente, competencia, agora);
            db.Resumos.Add(resumo);
        }

        resumo.Acumular(valorDocumento, agora);

        await db.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<ResumoDoEmitente>> ListarAsync(CancellationToken cancellationToken) =>
        await db.Resumos
            .AsNoTracking()
            .OrderByDescending(resumo => resumo.Competencia)
            .Select(resumo => new ResumoDoEmitente(
                resumo.CnpjEmitente,
                resumo.Competencia,
                resumo.QuantidadeDocumentos,
                resumo.ValorTotal,
                resumo.AtualizadoEm))
            .ToListAsync(cancellationToken);
}
