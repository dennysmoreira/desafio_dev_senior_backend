# Teste de carga — resultados

Medido em 2026-09-02, k6 em container contra a API rodando local (`dotnet run`,
Debug), PostgreSQL 17 em container, base com **50 mil documentos**. 10 VUs por
cenário, 20 segundos cada.

> Máquina de desenvolvimento, build Debug, tudo no mesmo host. Os números servem
> para **comparar decisões entre si**, não como capacidade de produção.

## Ingestão — `POST /documentos`

| Métrica | Valor |
|---|---|
| Vazão | 174 req/s |
| Latência média | 57 ms |
| p95 | 82 ms |
| Erros | 0% |
| Documentos criados | 100% dos envios (3.488 de 3.488) |

Cada iteração envia uma chave de acesso inédita, então o número mede inserção real:
leitura blindada do XML, parse, SHA-256, `INSERT` e publicação do evento.

## Consulta — `GET /documentos` com filtros + `GET /documentos/{id}`

Mesma consulta, mesma base, com e sem o índice composto
`ix_documento_fiscal_cnpj_data (CnpjEmitente, DataEmissao DESC)`:

| | Com índice | Sem índice |
|---|---|---|
| Latência média | 20,7 ms | 55,6 ms |
| **p95** | **41,2 ms** | **122,0 ms** |
| Vazão | 469 req/s | — |

**p95 quase 3× pior sem o índice**, na mesma consulta e no mesmo volume.

O plano confirma o motivo — com o índice, a consulta filtra e ordena percorrendo-o,
sem ordenação em memória:

```
Limit
  -> Index Scan using ix_documento_fiscal_cnpj_data on documento_fiscal
       Index Cond: (CnpjEmitente = ... AND DataEmissao >= ... AND DataEmissao <= ...)
       Filter: (NOT Excluido)
       Buffers: shared hit=22
```

Vinte e duas páginas lidas para devolver vinte registros de uma base de cinquenta mil.

## Por que não há cache

Nenhum cache de aplicação foi adicionado, e a decisão é deliberada:

- No volume medido, a consulta responde em ~20 ms de média. Não há gargalo para
  cachear — o índice já resolveu.
- Cache exige invalidação, e invalidação errada devolve dado velho. Trocar 20 ms
  por essa classe de bug é mau negócio.
- Um cache externo seria mais um ponto de falha, num desafio cujo item 8 pede
  justamente resiliência.

O que existe no lugar, a custo zero: **ETag** no detalhe, reaproveitando o SHA-256
já calculado na ingestão. Cliente que revisita um documento recebe `304` sem corpo.

## Como reproduzir

Com a API de pé e a base povoada:

```bash
docker exec -i fiscal-postgres psql -U fiscal -d fiscal < carga/povoar.sql
docker run --rm -i --add-host=host.docker.internal:host-gateway \
  -v "$PWD/carga:/carga" grafana/k6 run /carga/ingestao-e-consulta.js
```

Cenários isolados: `-e CENARIO=ingestao` ou `-e CENARIO=consulta`.
Ao repetir a ingestão, incremente `-e EXECUCAO=N` — sem isso a segunda rodada
reenvia as mesmas chaves, recebe `200` de replay em vez de `201`, e passa a medir o
caminho idempotente sem avisar.
