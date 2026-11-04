# Changelog

Todas as mudanças relevantes do NfseSaaS são registradas aqui, por versão.
Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/);
versionamento segue [SemVer](https://semver.org/lang/pt-BR/) (MAJOR.MINOR.PATCH).

## [Não lançado]
### Adicionado
- Tela "Índices econômicos" (menu lateral): variação mensal de IPCA, IGP-M,
  INCC, INPC, Selic e CDI, importada todo dia (09:40) da API SGS do Banco
  Central — 24 meses na primeira vez. Mostra o acumulado dos 12 últimos meses
  já publicados para qualquer data de aniversário, com os meses usados; recusa
  calcular quando falta mês ou o índice está desatualizado. O operador da
  plataforma pode lançar/corrigir um mês à mão (fica "Manual" e a importação
  não sobrescreve), excluir e "Importar agora". Base do reajuste automático.
- Histórico de valores do contrato: substitui o "Histórico de reajustes".
  Cada mudança de valor é um evento com tipo (Valor inicial, Reajuste,
  Alteração de escopo), data de vigência, valor anterior → novo, %, índice,
  motivo e os serviços cobrados a partir dele. Contrato novo já nasce com o
  evento de Valor inicial. A migration `HistoricoValoresContrato` converte
  os reajustes existentes (sem perda) e cria o Valor inicial de cada contrato.
- Editar os serviços de um contrato pede o tipo da mudança (reajuste ou
  alteração de escopo), a vigência e o motivo, e registra no histórico.

### Corrigido
- Telas de Empresas, Clientes, Serviços, Contratos e Notas Fiscais decidiam
  os botões de incluir/editar/emitir/cancelar pelo papel antigo do Identity
  ("Administrador"/"Emissor"), que contas criadas pelo cadastro e grupos
  personalizados não têm — esses usuários não viam os botões, embora a API
  permitisse. Agora usam a permissão do Grupo, a mesma que a API confere.
- Reajuste com data futura deixa de ser aceito (o valor novo passava a valer
  na hora, antes da data informada).

### Alterado
- Contratos: ao editar, a aba "Serviço" ficava vazia (o bloco era escondido).
  Agora mostra as linhas e permite incluir, remover e alterar quantidade e
  valor dos serviços de um Contrato existente. O valor do Contrato é
  recalculado e a alteração aparece no Histórico ("Serviços alterados:
  R$ X → R$ Y"). Notas já emitidas não mudam; reajuste por índice continua
  em "Registrar reajuste". Linha com Serviço desativado continua aparecendo
  (marcada "inativo").
- Retenção de ISS passou da Empresa para o Cliente ("Este cliente retém o
  ISS na fonte") e pode ser ajustada em cada nota. Com ISS retido, a DPS
  passa a enviar a alíquota (`pAliq`), da nota ou da nova "Alíquota de ISS
  (%)" da Empresa — antes uma Empresa marcada como "retido" emitia TODAS as
  notas retidas e sem alíquota. Nota Mensal e automação usam o padrão do
  Cliente. A migration `RetencaoIssPorCliente` marca como "retém ISS" os
  Clientes de Empresas que estavam com retenção.
- Tributação ISSQN restrita a "Operação tributável": imunidade, exportação
  e não incidência exigem grupos que a DPS ainda não monta (toda nota dessas
  Empresas seria rejeitada). Retenção pelo intermediário também bloqueada.

### Segurança
- Painel `/hangfire`, tela Logs e tela Sobre agora são exclusivos do
  operador da plataforma (`Plataforma:UsuariosOperadoresIds`). Antes
  qualquer Administrador de tenant — inclusive quem acabou de se cadastrar
  — via os logs de todos os clientes e podia disparar jobs (inclusive
  emissão) de outros tenants. As telas Logs e Sobre saem da matriz de
  permissões dos grupos (removidas na subida, em bancos existentes).
- Bloquear um usuário derruba a sessão aberta dele em até 1 minuto
  (antes valia até o cookie expirar), e "esqueci a senha" não desfaz mais
  um bloqueio feito pelo Administrador. Mudanças de grupo/permissão passam
  a valer em até 1 minuto, sem relogar.
- Emissão e cadastro de Contrato conferem se Cliente e Serviço pertencem
  à mesma Empresa.
- Chaves do Data Protection (que abrem os certificados dos clientes)
  podem ser criptografadas com um certificado próprio
  (`DataProtection:CertificadoPfxPath`/`CertificadoSenha`) e ter pasta
  própria (`DataProtection:KeysPath`). Compatível com as chaves atuais; a
  subida registra no log onde estão as chaves e se estão protegidas.

### Corrigido
- Envio de DPS e de evento de cancelamento não é mais reenviado
  automaticamente em falha de rede (retry só em consultas GET). Reenviar
  às cegas depois de um timeout podia gerar NFS-e em duplicidade.
- Sincronização ADN: nota que ficou em "falha de comunicação" mas foi
  autorizada pela SEFIN agora é confirmada com os dados oficiais (antes a
  nota autorizada era descartada como conflito de numeração). Notas em
  que a Empresa não é a emitente (ex.: recebidas de fornecedor) deixam de
  ser importadas.
- Cadastro de nova conta: Tenant e usuário são criados numa transação só
  (antes uma senha recusada deixava o CNPJ travado com erro 500), com CNPJ
  validado e mensagem clara para CNPJ já cadastrado. Novo
  `Plataforma:CadastroAberto` para fechar o auto-cadastro.
- Telefone da Empresa com máscara quebrava toda DPS (E1235); agora vai só
  com dígitos e o cadastro valida 6 a 20 dígitos.
- Evento de cancelamento usava o horário do servidor (UTC no container),
  mesmo problema que causava E0008 na emissão.
- Emissão em Homologação rejeitada com E1235 ("invalid child element
  'cLocEmi' ... expected: 'cMotivoEmisTI, finNFSe'") desde que a
  Produção Restrita passou a exigir o leiaute da NT 009 (esquemas de
  20/10/2026). A DPS agora sai com `finNFSe=0` (NFS-e regular) entre
  `tpEmit` e `cLocEmi`, e com `xDescServ` como último elemento de `cServ`.
- Tomador pessoa física (CPF) gerava `<CNPJ>` com 11 dígitos e a DPS era
  rejeitada (E1235). Agora sai `<CPF>`; CPF/CNPJ com máscara são limpos
  antes de ir pro XML (letras do CNPJ alfanumérico são preservadas).
- Empresa Não optante pelo Simples não conseguia emitir: o grupo
  obrigatório `totTrib` era omitido. Agora sai `indTotTrib=0`; optantes
  (MEI e ME/EPP) continuam com `pTotTribSN`.
- `regApTribSN` só é enviado para optante ME/EPP (antes ia para qualquer
  regime, inclusive vazio), e passou a ser obrigatório nesse caso.
- Inscrição municipal, telefone e e-mail do prestador vazios não geram
  mais elemento vazio no XML.
- Cadastro de Empresa: a lista "Regime especial de tributação" tinha os
  textos trocados em relação aos códigos enviados à SEFIN (ex.: "Sociedade
  de Profissionais" mandava o código 2, que é "Estimativa"). Textos agora
  seguem o XSD oficial (`TSRegEspTrib`), também no DANFSe, e o cadastro só
  aceita os códigos 0–6 e 9.
- Cadastro de Empresa: "CST PIS/COFINS" só oferecia 00–04; agora traz
  todos os CSTs de saída da tabela oficial (00–09, 49 e 99). Tributação
  e retenção de ISSQN e PIS/COFINS passaram a ser validadas contra os
  códigos do XSD.
- Emitir nota: ao escolher o Cliente, os Contratos dele não carregavam
  (o campo "Contrato" ficava vazio). O select2 dispara `change` via jQuery,
  que não chega a listener nativo (`addEventListener`). Agora usa `.on()`.
- Emitir nota: escolher um Contrato deixava o Serviço em branco (lia
  `contrato.servicoId`, que não existe desde que o Contrato passou a ter
  várias linhas de serviço). Agora seleciona a primeira linha, com o valor
  da linha; com mais de um serviço, avisa que a nota avulsa sai para o
  serviço escolhido e indica a Nota Mensal para faturar o contrato inteiro.
- DANFSe: "Retenção PIS/COFINS" só reconhecia os códigos 0 e 1 (com texto
  errado no 1); demais saíam "-". Agora cobre 0–9 conforme o XSD.
- DANFSe: campo "Finalidade" lê `finNFSe` da nova posição (raiz do
  `infDPS`, NT 009), com fallback para o local antigo em notas anteriores.

### Adicionado
- Leiaute da DPS configurável por ambiente
  (`NfseNacional:LeiauteDpsHomologacao` / `NfseNacional:LeiauteDpsProducao`,
  valores `Nt004` ou `Nt009`). Padrão: Homologação `Nt009`, Produção
  `Nt004`. Na entrada da NT 009 em Produção (previsão 01/12/2026), trocar
  só `LeiauteDpsProducao` para `Nt009`, sem deploy.
- Teste que valida a DPS gerada contra os XSDs oficiais de Produção
  Restrita (`tests/NfseSaaS.Tests/Schemas/ProducaoRestrita_20261020`).

## [1.2.0] - 2026-09-16
### Adicionado
- Snapshot fiscal da nota, na tela de detalhe, agora é colapsável
  (some por padrão, evita modal gigante).
- Toasts passaram a empilhar múltiplas mensagens (antes uma
  sobrescrevia a outra), com ícone de sucesso/erro, botão de fechar e
  barra de progresso.

### Alterado
- Todo modal passou a abrir centralizado verticalmente na tela (antes
  abria colado no topo), com corpo de altura limitada e scroll
  interno, cantos mais suaves e fundo com leve desfoque.

### Corrigido
- Botões de ação em tabela (editar, excluir, ativar/desativar etc., em
  Empresas, Clientes, Serviços, Contratos, Usuários, Grupos, E-mails e
  Nfse) não desabilitavam durante a requisição — clique repetido antes
  da tela atualizar disparava a mesma ação várias vezes seguidas.

## [1.1.0] - 2026-09-16
### Adicionado
- Proteção anti-CSRF (cookie de validação HttpOnly + cookie legível +
  header `X-CSRF-TOKEN`) em toda rota que muda estado, tela e API —
  conferida automaticamente, sem precisar decorar cada action.
- Cabeçalhos de segurança em toda resposta: `Content-Security-Policy`,
  `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`,
  `Permissions-Policy`.
- Rate limiting por IP nas rotas de login, registro, recuperação de
  senha e emissão de nota mensal em lote, mais um limite global de
  rede de segurança.
- Retry com backoff exponencial e circuit breaker (Polly) nas chamadas
  à SEFIN Nacional e ao ADN — uma instabilidade do lado deles não trava
  mais uma emissão em lote inteira.

### Alterado
- Todo JavaScript e CSS que estava inline nas views foi movido para
  arquivos externos em `wwwroot/`, permitindo uma
  `Content-Security-Policy` sem `'unsafe-inline'` em `script-src`.

## [1.0.1] - 2026-09-15
### Corrigido
- Descrição do serviço na emissão avulsa de Nfse era um campo de uma
  linha só, mesmo aceitando até 2000 caracteres; agora é um textarea.
- Contrato podia ser salvo com uma linha de Serviço vazia, quebrando o
  model binding no backend e devolvendo um 400 sem log nenhum; agora é
  barrado no formulário antes de enviar.
- NfseApiClient não logava o corpo da resposta quando a SEFIN rejeitava
  a emissão da DPS (só o cancelamento já fazia isso).
- DpsBuilder mandava `pTotTribSN` vazio quando a Empresa não tinha o
  percentual preenchido, quebrando o XSD da SEFIN (E1235); agora omite
  o campo quando não há valor.
- Cadastro de Empresa não exigia o percentual do Simples Nacional para
  MEI (só para ME/EPP), permitindo salvar cadastro de Optante incompleto.
- `dhEmi` da DPS era montado no fuso do servidor (UTC em produção) em
  vez de Brasília fixo, causando rejeição E0008 na SEFIN.

## [1.0.0] - 2026-09-12
### Adicionado
- Tela "Logs do sistema" (`/Logs`) — visualização dos logs de erro direto
  na UI, sem precisar de terminal/SSH. Restrita ao grupo Administrador.
- Tela "Sobre" (`/Sobre`) — versão em produção e histórico de mudanças
  visível pro Administrador.

### Corrigido
- Exceções não tratadas em produção não eram logadas (middleware de log
  nunca era alcançado — `UseExceptionHandler` engolia a exceção antes).
  Agora toda exceção é logada com stack trace completo e `CorrelationId`.
- Tela quebrada exibia JSON cru ou 404 em branco; agora mostra uma
  página de erro amigável com código de correlação pro suporte.

## [1.3.0] - 2026-09-16
### Adicionado
- Catálogo oficial de Código de Tributação Nacional (cTribNac, 338
  códigos) e de NBS — Nomenclatura Brasileira de Serviços (920
  códigos), alimentados a partir das tabelas oficiais do governo.
- Busca com autocomplete nos campos cTribNac e NBS do cadastro de
  Serviço, no lugar de digitação livre.

### Alterado
- `CodigoTributacaoNacional` e `CodigoNbs` do Serviço agora são
  validados contra o catálogo oficial antes de salvar (não só
  formato) — bloqueia código que a SEFIN rejeitaria de qualquer jeito.

### Corrigido
- Busca nos catálogos não encontrava termos que só apareciam com
  letra maiúscula na descrição (comparação estava case-sensitive).

## [1.4.0] - 2026-09-16
### Adicionado
- Campo Cliente com busca por CNPJ/CPF ou nome (autocomplete) nos
  modais de cadastro de Contrato e de emissão avulsa de Nfse.

### Alterado
- Modal de cadastro de Empresa reorganizado em abas (Dados gerais,
  Endereço, Regime tributário, Contratos), no lugar de uma coluna só.
- Campos de reajuste do Contrato (periodicidade, índice, alerta de
  antecedência) agora dependem do Tipo de cobrança: obrigatórios e
  editáveis para Mensal, zerados e bloqueados para Avulso — validado
  também no servidor.

### Corrigido
- Salvar um Contrato ou Empresa com campo obrigatório vazio numa aba
  diferente da atual não avisava nada (o navegador barra o envio sem
  mostrar aviso quando o campo inválido está escondido). Agora troca
  pra aba certa e mostra qual campo falta.
- Campo Cliente na emissão avulsa de nota carregava a lista inteira
  (até 200 clientes) e vinha com um selecionado sem o usuário escolher.