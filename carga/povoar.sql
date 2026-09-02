-- Povoa a base para o teste de carga ter o que consultar.
--
-- Inserção direta em SQL, e não pela API, de propósito: o que este script prepara é
-- o VOLUME para medir a leitura. Fazer 50 mil POSTs para depois medir SELECT
-- misturaria o custo da escrita no cenário de consulta.

INSERT INTO documento_fiscal (
    "Id", "Tipo", "ChaveAcesso", "Numero", "Serie",
    "CnpjEmitente", "NomeEmitente", "UfEmitente",
    "DocumentoDestinatario", "NomeDestinatario",
    "DataEmissao", "ValorTotal", "HashConteudo", "Observacao",
    "RecebidoEm", "AtualizadoEm", "Excluido", "ExcluidoEm")
SELECT
    gen_random_uuid(),
    1,
    '352601' || '12345678000199' || '55' || '001'
        || lpad(n::text, 9, '0') || '1' || lpad((n % 100000000)::text, 8, '0') || '0',
    n::text,
    '1',
    '12345678000199',
    'Comercio Exemplo Ltda',
    (ARRAY['SP', 'RJ', 'MG', 'RS', 'PR'])[1 + (n % 5)],
    lpad(((n * 7919) % 100000000000)::text, 11, '0'),
    'Cliente Numero ' || n,
    timestamptz '2026-01-01 00:00:00+00' + (n % 365) * interval '1 day',
    round((100 + (n % 9000))::numeric, 2),
    encode(sha256(n::text::bytea), 'hex'),
    NULL,
    now(),
    now(),
    false,
    NULL
FROM generate_series(1, 50000) AS n
ON CONFLICT DO NOTHING;

ANALYZE documento_fiscal;
