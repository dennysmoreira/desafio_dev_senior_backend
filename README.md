# API de documentos fiscais

Recebe XML de NF-e, persiste os dados, publica um evento no RabbitMQ e mantém um
resumo por emitente alimentado pelo consumidor. Expõe consulta, alteração e
exclusão sobre os documentos recebidos.

.NET 10 · PostgreSQL 17 · RabbitMQ 4 · EF Core 10 · NUnit + Testcontainers · k6

---

## Rodando

Há dois caminhos. Os dois deixam a API em `http://localhost:5099`.

### Tudo em container — só precisa de Docker

```bash
docker compose --profile completo up -d
```

Compila a API, sobe Postgres e RabbitMQ, espera os dois ficarem saudáveis e então
inicia. **Não exige o SDK do .NET instalado.** É o caminho mais previsível se você
quer só ver funcionando.

### API local — para depurar

```bash
docker compose up -d
dotnet run --project src/Fiscal.Api
```

Sobe só a infraestrutura em container e roda a API na sua máquina, abrindo a
documentação interativa no navegador. Exige o **SDK do .NET 10**.

Os dois convivem porque a API fica atrás de um profile do compose: sem isso, ela
ocuparia a 5099 e brigaria com o `dotnet run`.

### Em qualquer um dos dois

As migrations são aplicadas no start — não há passo manual de banco. **Na primeira
execução o log mostra um `fail` do EF Core** consultando `__EFMigrationsHistory`:
é esperado, a tabela ainda não existe numa base nova. Logo abaixo aparece
`Applying migration 'EsquemaInicial'`.

Para derrubar tudo, incluindo os dados: `docker compose --profile completo down -v`.

| Recurso | Onde |
|---|---|
| Documentação interativa | `/scalar/v1` |
| OpenAPI | `/openapi/v1.json` |
| Saúde | `/health` |
| Painel do RabbitMQ | `http://localhost:15672` — `fiscal` / `fiscal` |

**Sem Docker:** aponte `ConnectionStrings:Fiscal` e `ConnectionStrings:RabbitMq` em
`src/Fiscal.Api/appsettings.json` para instâncias suas. Se `RabbitMq` ficar vazia, a
API sobe com um publicador que apenas registra em log, avisa no start e não ativa o
consumidor — útil para ver a ingestão funcionando sem broker.

### Autenticação

Todas as rotas (exceto `/health`, `/openapi` e `/scalar`) exigem dois cabeçalhos:

```
X-Api-Key: chave-de-desenvolvimento
X-Cnpj: 12345678000199
```

Ver [Segurança e dados sensíveis](#segurança-e-dados-sensíveis) para o porquê de ser
tão simples.

---

## Endpoints

### `POST /documentos` — recebe o XML

Corpo cru, `Content-Type: application/xml`.

```bash
curl -X POST http://localhost:5099/documentos \
  -H "Content-Type: application/xml" \
  -H "X-Api-Key: chave-de-desenvolvimento" \
  -H "X-Cnpj: 12345678000199" \
  --data-binary @exemplos/nfe-exemplo.xml
```

`201 Created` na primeira vez, com `Location`:

```json
{
  "id": "01a05fca-0f98-70bd-8a11-c6a2e0ed7ba5",
  "tipo": "Nfe",
  "chaveAcesso": "35260112345678000199550010000000011123456780",
  "numero": "1",
  "serie": "1",
  "cnpjEmitente": "12345678000199",
  "nomeEmitente": "Comercio Exemplo Ltda",
  "ufEmitente": "SP",
  "documentoDestinatario": "***982247**",
  "dataEmissao": "2026-01-15T13:30:00+00:00",
  "valorTotal": 300.00,
  "quantidadeItens": 2,
  "hashConteudo": "0efd77c567805b46..."
}
```

| Situação | Resposta |
|---|---|
| Documento novo | `201` + `Location` |
| Mesmo XML reenviado | `200` + `X-Idempotent-Replay: true`, devolve o existente |
| Mesma chave, conteúdo diferente | `409` — nada é gravado |
| Emitente do XML ≠ CNPJ autenticado | `403` |
| XML inválido, XXE, layout desconhecido | `422` |
| Acima de 10 MB | `413` |

### `GET /documentos` — listagem

```bash
curl "http://localhost:5099/documentos?dataInicio=2026-01-01T00:00:00Z&documentoDestinatario=52998224725&uf=SP&pagina=1&tamanho=20" \
  -H "X-Api-Key: chave-de-desenvolvimento" -H "X-Cnpj: 12345678000199"
```

```json
{
  "itens": [
    {
      "id": "01a05fca-0f98-70bd-8a11-c6a2e0ed7ba5",
      "chaveAcesso": "35260112345678000199550010000000011123456780",
      "cnpjEmitente": "12345678000199",
      "ufEmitente": "SP",
      "documentoDestinatario": "***982247**",
      "nomeDestinatario": "Maria ********* ** *****",
      "dataEmissao": "2026-01-15T13:30:00+00:00",
      "valorTotal": 300.00,
      "observacao": null
    }
  ],
  "pagina": 1,
  "tamanho": 20,
  "total": 1,
  "totalPaginas": 1
}
```

Filtros: `dataInicio`, `dataFim`, `documentoDestinatario`, `uf`, `pagina`, `tamanho`
(padrão 20, teto 100).

**Não existe filtro por CNPJ do emitente** — ele já está fixado pela autenticação, e
o parâmetro seria inócuo. O filtro por CNPJ que produz resultado é o do destinatário:
*"quais notas emiti para o cliente X"*.

### `GET /documentos/{id}` — detalhe

Traz os itens e o CPF/CNPJ do destinatário **íntegro**. Devolve `ETag`; reenvie em
`If-None-Match` para receber `304`.

| Situação | Resposta |
|---|---|
| Encontrado | `200` + `ETag` |
| `If-None-Match` bate | `304` |
| Não existe, **ou é de outro CNPJ** | `404` |
| Excluído logicamente | `410` |

### `PUT /documentos/{id}` — altera a observação

```bash
curl -X PUT http://localhost:5099/documentos/{id} \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: chave-de-desenvolvimento" -H "X-Cnpj: 12345678000199" \
  -d '{"observacao":"conferido com o pedido 4471"}'
```

Alcança **apenas** a observação. Ver [imutabilidade](#1-o-documento-fiscal-é-imutável).

### `DELETE /documentos/{id}` — exclusão lógica

`204`. Repetir é no-op, também `204`.

### `GET /resumos` — o trabalho do consumidor

Agregado por emitente e competência, mantido pelo consumidor da fila:

```json
[{ "cnpjEmitente": "12345678000199", "competencia": "2026-01",
   "quantidadeDocumentos": 3, "valorTotal": 642.50,
   "atualizadoEm": "2026-09-02T02:45:03Z" }]
```

Não está no enunciado. Existe porque, sem ele, não há como observar pela API que o
consumidor fez alguma coisa.

---

## Decisões de arquitetura e modelagem

### 1. O documento fiscal é imutável

O enunciado pede "atualizar um documento existente". Documento fiscal autorizado
**não se altera**: corrige-se por carta de correção ou cancela-se, e ambos geram
outro XML.

A modelagem separa duas naturezas. O documento fiscal — chave, emitente,
destinatário, valores, itens — não tem um único setter público; o único método de
mutação é `AtualizarObservacao`. A observação é anotação interna de quem recebeu,
nasce do nosso processo e não do Fisco.

**A restrição é estrutural, não convencional: não existe caminho no código para
sobrescrever o documento.** Um teste por reflexão falha se alguém abrir um setter,
que é exatamente como a regra se perderia na prática.

Pela mesma razão, `DELETE` é exclusão lógica: o Fisco exige guarda de cinco anos, e
apagar a linha seria descumprir obrigação legal.

### 2. Idempotência apoiada no banco, nunca numa verificação prévia

A chave de acesso é a identidade natural. O índice único `(Tipo, ChaveAcesso)` é a
autoridade — **não há `SELECT` antes do `INSERT`**, porque entre verificar e gravar
existe uma janela em que várias requisições passam na verificação e todas tentam
gravar. O caminho feliz é uma única escrita; só quando o índice recusa é que vale a
pena consultar, para distinguir reenvio de divergência pelo SHA-256 do conteúdo.

Verificado: **20 POSTs simultâneos do mesmo XML produzem 1 `201` e 19 `200`.**

Detalhe deliberado: **o índice único não filtra exclusão lógica.** Se filtrasse,
reenviar o XML de um documento excluído criaria uma segunda linha para o mesmo
documento fiscal — a idempotência quebraria no caso menos óbvio.

### 3. Reenvio idêntico responde 200, não 409

Um cliente que gravou com sucesso mas perdeu a resposta — timeout, proxy, conexão
resetada — só pode reenviar. Se receber `409`, sua biblioteca de retry trata como
falha terminal e transforma uma ingestão bem-sucedida em alarme.

Além disso, `409` está reservado para o conflito real (mesma chave, conteúdo
diferente). Usar o mesmo código nos dois casos apagaria a distinção entre *"já está
feito"* e *"você mandou algo inconsistente"*. O header `X-Idempotent-Replay` deixa
explícito que nada foi criado.

### 4. Isolamento no `DbContext`, não no endpoint

O filtro por CNPJ é um Global Query Filter: toda consulta escrita daqui pra frente já
nasce isolada. Isolamento aplicado no endpoint vale até alguém esquecer.

Os filtros são **nomeados** (`ExclusaoLogica`, `IsolamentoPorCnpj`) para que desligar
um seja escolha localizável — um `IgnoreQueryFilters()` sem argumento derrubaria
também o isolamento.

Ler o CNPJ sem estar autenticado **lança exceção**. Uma consulta fora de contexto
autenticado falha, em vez de silenciosamente devolver dados de todos os
contribuintes. O consumidor da fila, que é cross-tenant por natureza, abre um escopo
de sistema explícito, e cada abertura é registrada em log — é o rastro de auditoria
do acesso cross-tenant.

Documento de outro CNPJ responde `404`, não `403`: um `403` confirmaria que o
registro existe.

### 5. Persistência relacional; o XML não é armazenado

PostgreSQL relacional, sem MongoDB. O que a API consulta são metadados fiscais com
filtros compostos e ordenação — território de índice composto e restrição única, que
é justamente onde o modelo relacional é forte. A restrição única, aliás, não é um
detalhe de storage aqui: **é o mecanismo de idempotência**.

O XML original **não é guardado** — só os dados extraídos e o SHA-256 do conteúdo.
A escolha é consciente e tem custo, que fica registrado:

- a assinatura digital da NF-e é calculada sobre o XML canonicalizado e **não pode
  ser reconstruída** a partir dos campos parseados. Sem o arquivo não há como provar
  autenticidade nem atender auditoria que peça o documento;
- os campos do layout que este parser não extrai são descartados na ingestão, sem
  possibilidade de reprocessamento se o parser precisar evoluir.

A idempotência não é afetada: ela se apoia no hash, que continua sendo calculado e
persistido — e que serve também de ETag.

### 6. Camadas com dependências verificadas

`Domain` → `Application` → `Infrastructure` → `Api`. O domínio não tem **um único
`PackageReference`**. EF Core, Npgsql e `RabbitMQ.Client` existem só na
infraestrutura — é por isso que a composição do EF mora numa extensão dentro dela: se
a API chamasse `AddDbContext`, passaria a depender do EF.

Cinco regras de NetArchTest guardam isso, e **cada uma foi confirmada injetando uma
violação real e vendo o teste falhar**.

Sem MediatR e sem CQRS: são cinco endpoints. Casos de uso são classes simples
injetadas.

### 7. Extensibilidade de layout provada, não prometida

`IParserDocumentoFiscal` + seleção pelo elemento raiz. Só NF-e está implementada.
CT-e e NFS-e não foram feitos — em vez de entregar um CT-e superficial, há um teste
que registra um parser de um layout inexistente e comprova que o pipeline o
seleciona **sem alterar uma linha** do seletor ou da ingestão.

### 8. Resiliência no consumo: dois níveis e uma distinção

| Situação | O que acontece |
|---|---|
| Falha transitória (banco reiniciando) | Retry in-process, backoff exponencial com jitter, **teto de 10s no conjunto** |
| Persiste após o teto | Vai para `fiscal.documentos.retry`, fila sem consumidor com TTL, que a devolve à principal |
| Erro permanente (XML inválido) | Direto para `fiscal.documentos.poison`, **zero tentativas** |
| Tentativas esgotadas (`x-death` ≥ 3) | Fila venenosa |

A distinção entre transitório e permanente é o ponto. Retentar um XML malformado dez
vezes só atrasa a fila e polui o log — por isso `DomainException` fica fora do
`ShouldHandle` do Polly, e por isso `XmlException` é traduzida em erro de domínio na
fronteira, em vez de escapar como `500`.

**O teto de 10s veio de uma medição.** Sem ele, cada tentativa de conexão do Npgsql a
um banco fora do ar espera 15s: três tentativas seguravam o consumidor por ~60s numa
única mensagem, e com prefetch 10 as outras nove ficavam presas atrás dela — um
mecanismo de resiliência produzindo indisponibilidade. Com o teto: 12s.

Idempotência do consumo por **inbox**: o registro da mensagem e o efeito colateral
são gravados na mesma transação. Se o processo morrer entre os dois, o rollback
desfaz ambos e a reentrega funciona — caso que uma verificação fora de transação não
cobre. A identidade da mensagem é o hash do XML, não um GUID por publicação, então a
mesma ingestão sempre produz a mesma identidade.

O agregado do resumo foi escolhido de propósito por ser um **acumulador**: se o
consumidor não fosse idempotente, a reentrega inflaria a soma silenciosamente. O
defeito apareceria nos números, não numa exceção.

---

## Segurança e dados sensíveis

### XXE e ataques via XML

O XML vem de fonte não confiável. Existe **uma única** configuração de leitura no
projeto — não há `XmlReader.Create` solto — e ela fecha as três famílias clássicas:

| Ataque | Defesa |
|---|---|
| **XXE** — entidade externa lendo `file:///etc/passwd` ou batendo em URL interna | `DtdProcessing.Prohibit`, sem resolver |
| **Billion laughs** — entidades recursivas expandindo para gigabytes | mesmo `Prohibit` + `MaxCharactersFromEntities = 0` |
| **Documento gigante** | teto de caracteres, mais limite de 10 MB no Kestrel e no endpoint |

A recusa vira `422`, não `500`. A diferença importa duas vezes: na API separa erro do
cliente de falha do servidor; no consumidor separa erro permanente, que vai direto
para a dead-letter, de transitório, que merece retry. Deixar `XmlException` escapar
faria um ataque recusado parecer indisponibilidade do serviço.

### Dado pessoal

Uma NF-e carrega CPF, nome e endereço do destinatário.

- **Mascarado na listagem, íntegro no detalhe.** Exposição em massa é o risco;
  consulta individual autorizada é uso legítimo. O mascaramento acontece no caso de
  uso, não no endpoint, para que nenhum consumidor da listagem — inclusive um log de
  diagnóstico — veja o valor em claro.
- **Todo acesso ao detalhe com destinatário preenchido é registrado em log**, com o
  CNPJ do solicitante mascarado.
- **Nada de dado pessoal em log de erro.** Erro não tratado vira `ProblemDetails`
  genérico; sem isso o Kestrel em desenvolvimento devolveria stack trace, caminho de
  arquivos e nomes internos.
- **Isolamento por contribuinte** aplicado no modelo (decisão 4).

### LGPD × obrigação fiscal

Estes dados **não** estão sob consentimento: a base legal é obrigação legal. O
direito de eliminação do titular não se aplica enquanto durar o prazo de guarda de
cinco anos — e é por isso que a exclusão é lógica, e não física. Um pedido de
eliminação seria atendido com a explicação da base legal, não com o `DELETE`.

### Autenticação

Deliberadamente simples: cabeçalho `X-Cnpj` mais `X-Api-Key` conferida em **tempo
constante** (comparar segredo com `==` vaza informação pelo tempo de resposta).

Em produção isto seria um JWT com o CNPJ numa claim. A escolha é consciente: o que
está sendo demonstrado é o **isolamento**, que vive no filtro global do `DbContext` e
não muda em nada com a troca do mecanismo de autenticação. Gastar o orçamento
montando emissão de token não melhoraria a parte que importa.

---

## Testes

```bash
dotnet test                              # tudo
dotnet test tests/Fiscal.UnitTests       # não precisa de nada
dotnet test tests/Fiscal.ArchitectureTests
dotnet test tests/Fiscal.IntegrationTests   # exige Docker
```

**70 testes** — 44 unitários, 21 de integração e 5 de arquitetura. Os de integração usam Testcontainers com PostgreSQL real — nada de
banco em memória, porque índice único, filtro global e exclusão lógica só existem no
banco de verdade, e são justamente o que precisa ser provado.

Sem Docker, rode os dois primeiros projetos: eles cobrem parser, blindagem contra
XXE, regras do domínio, as quatro saídas da ingestão e as regras de arquitetura.

Os que carregam mais peso:

| Teste | O que garante |
|---|---|
| `Envios_simultaneos_do_mesmo_xml_gravam_uma_linha_so` | 20 POSTs paralelos → 1 `201`, 19 `200`. Mata a implementação com `SELECT` antes do `INSERT` |
| `Nenhuma_propriedade_do_documento_tem_setter_publico` | Guarda a imutabilidade por reflexão |
| `Excluir_esconde_da_listagem_mas_a_chave_continua_ocupada` | Reenviar XML de documento excluído não recria a linha |
| `Seleciona_um_layout_novo_apenas_por_registro_no_container` | Extensibilidade sem tocar no pipeline |
| `A_mesma_mensagem_entregue_duas_vezes_conta_o_documento_uma_vez` | Inbox do consumidor |
| `Consultar_documento_de_outro_contribuinte_devolve_404_e_nao_403` | Isolamento sem vazar existência |

O fluxo de mensageria também foi verificado manualmente contra broker real: mensagem
ilegível indo para a fila venenosa sem tentativas, e mensagem válida publicada com o
banco parado indo para a fila de espera, voltando após o TTL e sendo processada.

### Carga

Números e método em [`carga/RESULTADOS.md`](carga/RESULTADOS.md). Resumo: ingestão a
174 req/s com p95 de 82 ms; consulta com p95 de **41 ms com o índice composto contra
122 ms sem ele**, na mesma base de 50 mil documentos.

**Não há cache de aplicação, e o número acima é o argumento.** No volume medido a
consulta responde em ~20 ms de média — não existe gargalo para cachear. Cache exige
invalidação, e invalidação errada devolve dado velho; trocar 20 ms por essa classe de
bug seria mau negócio, ainda mais num sistema cujo requisito é resiliência. O que
existe no lugar, a custo zero, é o **ETag** reaproveitando o SHA-256 já calculado na
ingestão.

---

## Se tivesse mais tempo

**Guardar o XML original em armazenamento de objetos.** É a lacuna mais séria
(decisão 5): sem o arquivo não há verificação de assinatura nem reprocessamento. O
desenho correto é blob fora do banco, com hash e referência no registro.

**Paginação por keyset.** `OFFSET` degrada linearmente com a profundidade da página —
a página 5.000 lê e descarta 100 mil linhas. Paginar por `(DataEmissao, Id)` mantém o
custo constante. Junto com isso, deixar de contar: o `COUNT(*)` é uma segunda ida ao
banco de custo próximo ao da página, e "tem próxima página" resolve a maioria dos
casos.

**Ingestão assíncrona.** Hoje o `POST` parseia e grava sincronamente. Em volume alto,
o desenho é aceitar o arquivo, responder `202` e parsear no consumidor — o mesmo
mecanismo de resiliência já construído passaria a proteger também a ingestão.

**CT-e e NFS-e de verdade.** A costura está pronta e testada; falta escrever os
parsers. NFS-e é o caso difícil, com layout variando por município.

**JWT e autorização por papel.** Hoje qualquer chave válida faz tudo dentro do seu
CNPJ.

**Observabilidade.** Logs estruturados existem, mas faltam métricas e tracing
distribuído — sem eles, diagnosticar uma mensagem presa na fila em produção é
arqueologia.

**Segredos fora do compose.** A chave de API e as senhas do banco e do broker estão
em texto no `docker-compose.yml` e no `appsettings.json`, o que serve para um
ambiente de avaliação e para nada além disso.
