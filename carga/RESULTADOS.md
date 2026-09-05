# Teste de carga — resultados

Medido em 2026-09-05, k6 em container contra a stack completa em containers na mesma
máquina (build Release para as imagens, PostgreSQL 17, RabbitMQ 4, MinIO), base com
**50 mil documentos** pré-existentes. 10 VUs por cenário, 20 segundos cada.

> Máquina de desenvolvimento, tudo no mesmo host. Os números servem para **comparar
> decisões entre si**, não como capacidade de produção.

## Ingestão — `POST /lotes`

Cada requisição envia um lote de **3 arquivos**.

| Métrica | Valor |
|---|---|
| Lotes aceitos | 142 lotes/s |
| Arquivos aceitos | ~427 arquivos/s |
| Latência média | 70 ms |
| p95 | 80 ms |
| Erros | 0% |
| Lotes com `202` | 100% |

O caminho síncrono faz hash, gravação no MinIO e uma transação com o lote, os itens e
os eventos de outbox. **Não faz parse** — é isso que mantém a resposta curta mesmo
com três arquivos por requisição.

## Consulta — `GET /documentos` + `GET /documentos/{id}`

Mesma consulta, mesma base, com e sem o índice composto
`ix_documento_fiscal_cnpj_data (CnpjEmitente, DataEmissao DESC)`:

| | Com índice | Sem índice |
|---|---|---|
| Latência média | 20,2 ms | 61,5 ms |
| **p95** | **40,6 ms** | **136,0 ms** |
| Vazão | 482 req/s | 161 req/s |

**p95 mais de 3× pior sem o índice**, e um terço da vazão, na mesma consulta e no
mesmo volume. O plano confirma: com o índice, a consulta filtra e ordena percorrendo-o,
lendo 22 páginas para devolver 20 registros de uma base de 50 mil.

## Vazão do worker, e onde estava o gargalo

Aqui a medição pagou por si. Com uma réplica de worker e milhares de itens pendentes,
a fila do RabbitMQ estava **praticamente vazia** — o que só faz sentido se o problema
for publicar, não consumir.

Era: o publicador abria um canal AMQP **por mensagem**. Canal é caro de criar e feito
para ser reaproveitado. Depois de reaproveitá-lo, a fila passou a acumular milhares de
mensagens, ou seja, o relay deixou de ser o limite.

| Configuração | Itens processados |
|---|---|
| 1 worker | 61 arquivos/s |
| 4 workers | ≥ 100 arquivos/s — a fila de 3.005 pendentes zerou antes da janela de 30s fechar |

O número de 4 workers é **piso**, não teto: o backlog acabou antes da medição
terminar. O que ele demonstra é o ponto do desenho — a vazão de consumo escala
acrescentando réplicas de worker, sem tocar na API.

E a assimetria é intencional: a API aceita ~427 arquivos/s e um worker processa ~61.
Numa rajada, a diferença vira fila em vez de erro — que é exatamente o motivo de a
ingestão ser assíncrona.

## Por que não há cache

- No volume medido, a consulta responde em ~20 ms de média. Não há gargalo para
  cachear — o índice já resolveu.
- Cache exige invalidação, e invalidação errada devolve dado velho.
- Seria mais um ponto de falha num sistema cujo requisito é resiliência.

O que existe no lugar, a custo zero: **ETag** no detalhe, reaproveitando o SHA-256 já
calculado na ingestão. Cliente que revisita um documento recebe `304` sem corpo.

## Como reproduzir

Com a stack de pé (`docker compose --profile completo up -d`) e a base povoada:

```bash
docker exec -i fiscal-postgres psql -U fiscal -d fiscal < carga/povoar.sql

docker run --rm -i --add-host=host.docker.internal:host-gateway \
  -v "$PWD/carga:/carga" grafana/k6 run /carga/ingestao-e-consulta.js
```

**No Git Bash do Windows**, prefixe com `MSYS_NO_PATHCONV=1`. Sem isso o MSYS
converte também o caminho de dentro do container, e o k6 acaba procurando o script em
`C:/Program Files/Git/carga`:

```bash
MSYS_NO_PATHCONV=1 docker run --rm -i --add-host=host.docker.internal:host-gateway \
  -v "$PWD/carga:/carga" grafana/k6 run /carga/ingestao-e-consulta.js
```

**No PowerShell**, troque o volume por `-v "${PWD}\carga:/carga"`.

Cenários isolados: `-e CENARIO=ingestao` ou `-e CENARIO=consulta`.
Ao repetir a ingestão, incremente `-e EXECUCAO=N` — sem isso a segunda rodada reenvia
as mesmas chaves, os itens viram `Duplicado` em vez de `Ingerido`, e a medição passa a
ser de outro caminho sem avisar.

Detalhe do script: os arquivos do multipart usam nomes de campo distintos
(`arquivo1`, `arquivo2`, `arquivo3`). O k6 não monta multipart a partir de um array
sob a mesma chave — a requisição sai malformada e o servidor devolve `415`.
