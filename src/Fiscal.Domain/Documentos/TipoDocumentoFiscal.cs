namespace Fiscal.Domain.Documentos;

public enum TipoDocumentoFiscal
{
    Nfe = 1,
    Cte = 2,
    Nfse = 3,
}

/// <summary>
/// Estado de conciliação do documento no ERP de quem recebeu. É metadado de
/// gestão — nasce do nosso processo, não do XML — e por isso é mutável.
/// </summary>
public enum StatusConciliacao
{
    Pendente = 0,
    Conciliado = 1,
    Divergente = 2,
}
