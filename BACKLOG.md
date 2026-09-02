# Backlog

Documento de trabalho. **Não faz parte da entrega** — apagar antes de enviar o link, ou manter se você achar que mostra organização.

Coluna **Origem**: `PDF` = exigência literal do enunciado · `Proposto` = ideia do Claude, pode ser cortada sem prejuízo ao requisito.

---

## Situação

| | |
|---|---|
| Orçamento total | < 10h |
| Já gasto (fundação) | ~1h30 |
| Restante disponível | ~8h30 |
| Backlog abaixo soma | **~9h55** |
| **Déficit** | **~1h25** |

O backlog não cabe. A seção "Como fechar o déficit" no fim propõe os cortes.

---

## Feito

| # | Item | Origem | Estado |
|---|---|---|---|
| F1 | 7 projetos em camadas, build limpo com `TreatWarningsAsErrors` | Proposto | ✅ |
| F2 | `docker-compose` com Postgres + RabbitMQ | PDF (item 2) | ✅ |
| F3 | Entidades `FiscalDocument` + `FiscalDocumentItem` | PDF (item 2) | ✅ |
| F4 | `FiscalDbContext`, índices, migration aplicada | PDF (item 2) | ✅ |
| F5 | Índice único `(Tipo, ChaveAcesso)` — base da idempotência | PDF (item 7) | ✅ |
| F6 | Filtro global de exclusão lógica e CNPJ | PDF (itens 4, 6) | ✅ |
| F7 | Helpers de mascaramento `DadosSensiveis` | PDF (item 6) | ✅ |
| F8 | Entidade `ResumoEmitente` | PDF (3) | ✅ |
| F9 | Entidade `MensagemProcessada` (inbox) | Proposto | ✅ |
| F10 | Campo `Observacao` (único mutável) | Proposto | ✅ |
| F11 | Nomenclatura padronizada em português | Proposto | ✅ |
| F12 | Escopo de sistema explícito + log no contexto de acesso | Proposto | ✅ |

---

## A fazer

### Ingestão e persistência

| # | Tarefa | Origem | Est. |
|---|---|---|---|
| T1 | `POST /documentos` recebendo XML | PDF (1) | 0:30 |
| T2 | `XmlReaderSettings` bloqueando DTD/XXE + limite de tamanho | PDF (4-segurança) | 0:20 |
| T3 | `IFiscalDocumentParser` + `NfeParser` (ide, emit, dest, total, det) | PDF (1) | 0:40 |
| T4 | SHA-256 do XML + idempotência por violação de índice único | PDF (7) | 0:30 |

### Endpoints REST

| # | Tarefa | Origem | Est. |
|---|---|---|---|
| T5 | `GET /documentos` — paginação + filtros data/CNPJ/UF, projeção sem o XML | PDF (4) | 0:40 |
| T6 | `GET /documentos/{id}` — detalhe | PDF (4) | 0:20 |
| T7 | ETag + `304` reaproveitando o SHA-256 | Proposto | 0:15 |
| T8 | `PUT /documentos/{id}` | PDF (4) | 0:20 |
| T9 | `DELETE /documentos/{id}` — exclusão lógica | PDF (4) | 0:15 |

### Segurança e dados sensíveis

| # | Tarefa | Origem | Est. |
|---|---|---|---|
| T10 | `IContextoAcesso` implementado + middleware de autenticação | PDF (4, 6) | 0:20 |
| T11 | Mascaramento aplicado em log e na listagem | PDF (6) | 0:20 |

### Mensageria

| # | Tarefa | Origem | Est. |
|---|---|---|---|
| T12 | Publisher: evento por documento processado | PDF (3) | 0:30 |
| T13 | Consumidor fazendo algo útil (hoje: alimentar `ResumoEmitente`) | PDF (3) | 0:30 |
| T14 | Retry com backoff + dead-letter + poison queue | PDF (8) | 0:30 |
| T15 | Distinguir erro transitório de permanente | PDF (8) | incluso T14 |

### Testes

| # | Tarefa | Origem | Est. |
|---|---|---|---|
| T16 | Unitários: parser, hash, mascaramento, regras do domínio | PDF (5) | 0:50 |
| T17 | Integração com Testcontainers: mesmo XML 2× = 1 registro | PDF (5, 7) | 0:40 |
| T18 | Integração: consumidor processa e resumo bate | PDF (5, 3) | 0:30 |
| T19 | Integração: CNPJ A não enxerga documento de CNPJ B | PDF (5, 6) | incluso T17 |

### Documentação e entrega

| # | Tarefa | Origem | Est. |
|---|---|---|---|
| T20 | OpenAPI/Scalar com exemplos de request e response | PDF (9) | 0:20 |
| T21 | README: como rodar app e testes | PDF (entrega) | 0:20 |
| T22 | README: decisões de arquitetura e modelagem | PDF (entrega) | 0:25 |
| T23 | README: como tratou dados sensíveis | PDF (entrega) | 0:10 |
| T24 | README: melhorias se tivesse mais tempo | PDF (entrega) | 0:05 |

### Opcionais (pontos extras)

| # | Tarefa | Origem | Est. |
|---|---|---|---|
| T25 | Teste de arquitetura (NetArchTest, 4 regras) | PDF (opcional) | 0:30 |
| T26 | Teste de carga k6 + tabela de números no README | PDF (opcional) | 0:40 |
| T27 | Cenário k6 concorrente provando a idempotência | Proposto | incluso T26 |

---

## Fora de escopo (decidido)

| Item | Motivo |
|---|---|
| Parser real de CTe e NFSe | Custa ~2h. Entrega no lugar `IFiscalDocumentParser` + teste com parser falso provando a extensibilidade |
| Redis / cache de aplicação | Não cabe, e vira ponto único de falha num desafio cujo item 8 é resiliência |
| JWT completo | Trocado por autenticação simples; o ponto avaliado é o filtro no `DbContext` |
| Paginação keyset | Offset com teto; keyset vira linha em "melhorias futuras" |
| MediatR / CQRS | Over-engineering para 5 endpoints |
| Criptografia de campo em repouso | Você não selecionou |

---

## Decisões fechadas

| # | Decisão | Resolução |
|---|---|---|
| D1 | O que o `PUT` altera | Só `Observacao`. `Status`, `Tags` e `MotivoExclusao` removidos do domínio e do schema |
| D2 | O que o consumidor faz | Resumo por emitente e competência (`ResumoEmitente`) |
| D3 | Idempotência no consumo | Tabela de inbox (`MensagemProcessada`) na mesma transação |
| D4 | Nomenclatura | Tudo em português. Sufixos de framework (`DbContext`, `Configuration`) mantidos |
| D5 | Escapatória do filtro global | Fechada: ler CNPJ sem autenticar lança; escopo de sistema é explícito e logado |
| D6 | Autenticação | Header `X-Cnpj` + chave de API |
| D7 | Este arquivo na entrega | Não. Apagar antes de enviar o link |

---

## Como fechar o déficit de ~1h25

Opções, em ordem de preferência:

1. **Eu escrevo, você revisa** (−0h00 no papel, mas é o que realmente muda o resultado). O backlog aprovado é o que me permite escrever sem decidir por você.
2. Remover os campos inventados (D1) simplifica T8 — **−0:10**
3. T18 (integração do consumidor) vira teste unitário com fila falsa — **−0:20**
4. T16 cobre só parser e idempotência, não o domínio inteiro — **−0:15**
5. Se ainda faltar: **T26 (k6) sai**, e com ele o segundo opcional — **−0:40**

Cortando 2, 3 e 4: sobra ~9h10 contra 8h30 disponíveis. Ainda 40min no vermelho — que é exatamente o custo do k6.
