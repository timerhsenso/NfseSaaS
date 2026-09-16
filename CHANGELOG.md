# Changelog

Todas as mudanças relevantes do NfseSaaS são registradas aqui, por versão.
Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/);
versionamento segue [SemVer](https://semver.org/lang/pt-BR/) (MAJOR.MINOR.PATCH).

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