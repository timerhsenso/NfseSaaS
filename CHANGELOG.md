# Changelog

Todas as mudanças relevantes do NfseSaaS são registradas aqui, por versão.
Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/);
versionamento segue [SemVer](https://semver.org/lang/pt-BR/) (MAJOR.MINOR.PATCH).

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
