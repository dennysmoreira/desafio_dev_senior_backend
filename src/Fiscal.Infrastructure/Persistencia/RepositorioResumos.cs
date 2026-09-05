using Fiscal.Application.Resumos;
using Fiscal.Domain.Resumos;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Infrastructure.Persistencia;

public sealed class RepositorioResumos(FiscalDbContext db) : IRepositorioResumos
{
    public async Task AcumularAsync(
        string cnpjEmitente,
        string competencia,
        decimal valorDocumento,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        // Ignora só o isolamento por CNPJ: o consumidor atende todos os contribuintes
        // e precisa alcançar o resumo do emitente do documento, seja ele quem for.
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

        // Não salva: quem fecha a transação é o caso de uso, depois de avançar
        // também o item e o lote.
    }

    public async Task<IReadOnlyList<ResumoDoEmitente>> ListarAsync(CancellationToken cancellationToken) =>
        await db.Resumos
            .AsNoTracking()
            .OrderBy(r => r.CnpjEmitente).ThenByDescending(r => r.Competencia)
            .Select(r => new ResumoDoEmitente(
                r.CnpjEmitente, r.Competencia, r.QuantidadeDocumentos, r.ValorTotal, r.AtualizadoEm))
            .ToListAsync(cancellationToken);
}
