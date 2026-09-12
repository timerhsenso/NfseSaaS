# Changelog

Todas as mudanças relevantes do NfseSaaS são registradas aqui, por versão.
Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/);
versionamento segue [SemVer](https://semver.org/lang/pt-BR/) (MAJOR.MINOR.PATCH).

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
