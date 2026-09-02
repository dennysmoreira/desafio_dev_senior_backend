import http from 'k6/http';
import { check } from 'k6';

// Roda contra a API já de pé. Dentro do container do k6, localhost é o próprio
// container — por isso host.docker.internal.
const BASE = __ENV.BASE_URL || 'http://host.docker.internal:5099';
const CHAVE_API = __ENV.API_KEY || 'chave-de-desenvolvimento';
const CNPJ = __ENV.CNPJ || '12345678000199';

const CABECALHOS_XML = {
  'Content-Type': 'application/xml',
  'X-Api-Key': CHAVE_API,
  'X-Cnpj': CNPJ,
};

const CABECALHOS_JSON = {
  'X-Api-Key': CHAVE_API,
  'X-Cnpj': CNPJ,
};

// Identifica a execução. Sem isto, uma segunda rodada reenvia as mesmas chaves da
// primeira e recebe 200 de replay em vez de 201 — o teste passaria a medir o
// caminho idempotente sem avisar.
const EXECUCAO = Number(__ENV.EXECUCAO || 1);

// Permite medir os cenários separadamente, que é o necessário para comparar a
// consulta com e sem índice.
const CENARIO = __ENV.CENARIO || 'ambos';

const ingestao = {
  executor: 'constant-vus',
  vus: 10,
  duration: '20s',
  exec: 'ingerir',
  tags: { cenario: 'ingestao' },
};

const consulta = {
  executor: 'constant-vus',
  vus: 10,
  duration: '20s',
  exec: 'consultar',
  tags: { cenario: 'consulta' },
};

function cenarios() {
  if (CENARIO === 'ingestao') return { ingestao };
  if (CENARIO === 'consulta') return { consulta };
  return { ingestao, consulta: { ...consulta, startTime: '22s' } };
}

export const options = {
  scenarios: cenarios(),
  // Só declara limite para o cenário que vai rodar: um threshold sem amostra é
  // contado como falha e faria o k6 sair com erro numa medição bem-sucedida.
  thresholds: Object.assign(
    { 'http_req_failed': ['rate<0.01'] },
    CENARIO !== 'consulta' ? { 'http_req_duration{cenario:ingestao}': ['p(95)<1000'] } : {},
    CENARIO !== 'ingestao' ? { 'http_req_duration{cenario:consulta}': ['p(95)<300'] } : {},
  ),
};

// cUF(2) AAMM(4) CNPJ(14) mod(2) serie(3) nNF(9) tpEmis(1) cNF(8) DV(1) = 44
function chaveDeAcesso(numero) {
  const nNF = String(numero).padStart(9, '0');
  const cNF = String(numero % 100000000).padStart(8, '0');
  return `352601${CNPJ}55001${nNF}1${cNF}0`;
}

function nfe(numero) {
  return `<?xml version="1.0" encoding="UTF-8"?>
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
  <NFe><infNFe Id="NFe${chaveDeAcesso(numero)}" versao="4.00">
    <ide><cUF>35</cUF><mod>55</mod><serie>1</serie><nNF>${numero}</nNF>
      <dhEmi>2026-01-15T10:30:00-03:00</dhEmi><tpNF>1</tpNF></ide>
    <emit><CNPJ>${CNPJ}</CNPJ><xNome>Comercio Exemplo Ltda</xNome>
      <enderEmit><UF>SP</UF></enderEmit></emit>
    <dest><CPF>52998224725</CPF><xNome>Maria Aparecida de Souza</xNome></dest>
    <det nItem="1"><prod><cProd>SKU-001</cProd><xProd>Caderno universitario</xProd>
      <NCM>48201000</NCM><CFOP>5102</CFOP>
      <qCom>10.0000</qCom><vUnCom>15.5000</vUnCom><vProd>155.00</vProd></prod></det>
    <total><ICMSTot><vNF>155.00</vNF></ICMSTot></total>
  </infNFe></NFe>
</nfeProc>`;
}

export function ingerir() {
  // Faixa alta, separada por execução e por VU: não colide com o seed nem com
  // rodadas anteriores, então toda ingestão é de fato uma inserção.
  const numero = (100000000 + EXECUCAO * 1000000 + __VU * 10000 + __ITER) % 1000000000;

  const resposta = http.post(`${BASE}/documentos`, nfe(numero), { headers: CABECALHOS_XML });

  check(resposta, {
    'ingestao criou o documento': (r) => r.status === 201,
  });
}

export function consultar() {
  // Exatamente os filtros que o enunciado pede, sobre a base povoada pelo seed.
  const listagem = http.get(
    `${BASE}/documentos?dataInicio=2026-01-01T00:00:00Z&dataFim=2026-12-31T23:59:59Z&tamanho=20`,
    { headers: CABECALHOS_JSON },
  );

  check(listagem, {
    'listagem respondeu 200': (r) => r.status === 200,
    'listagem trouxe itens': (r) => r.json('itens').length > 0,
  });

  const primeiro = listagem.json('itens')[0];

  if (primeiro) {
    const detalhe = http.get(`${BASE}/documentos/${primeiro.id}`, { headers: CABECALHOS_JSON });

    check(detalhe, { 'detalhe respondeu 200': (r) => r.status === 200 });
  }
}
