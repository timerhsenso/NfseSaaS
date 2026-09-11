# NfseSaaS

SaaS multiempresa (multi-tenant) para emissão de NFS-e através do Sistema
Nacional da NFS-e / SEFIN Nacional.

> **Status:** Fases 1 a 7 concluídas — fundação da arquitetura, integração
> fiscal real com a SEFIN (assinatura de DPS via mTLS, emissão, cancelamento
> e sincronização de eventos), CRUDs completos (Empresas, Clientes,
> Serviços), Contratos com múltiplos serviços por contrato, reajuste,
> documentos anexos e emissão mensal em lote. Fase 8 (dashboard, MRR,
> webhooks, API pública, portal do cliente) adiada conscientemente — só
> entra com demanda real confirmada.

## Objetivo do projeto

Transformar uma POC .NET 8 já validada ponta a ponta com a SEFIN Nacional
(carregamento de certificado A1, mTLS, geração/assinatura de DPS,
GZip+Base64, POST /nfse, tratamento de resposta — HTTP 201 obtido) em uma
base de código profissional, multiempresa, pronta para evoluir para um
produto SaaS comercial.

## Arquitetura

```
Domain  ←  Application  ←  Web
                              ↑
                     Infrastructure (implementa abstrações)

NfseSaaS.Nacional — isolado, sem depender de Domain/EF Core/ASP.NET
```

- **NfseSaaS.Domain** — entidades, enums, sem dependências externas.
- **NfseSaaS.Application** — casos de uso (DTOs/interfaces), não conhece EF Core/PostgreSQL.
- **NfseSaaS.Infrastructure** — EF Core, PostgreSQL, Identity, multi-tenancy (Global Query Filters).
- **NfseSaaS.Nacional** — integração fiscal com a SEFIN Nacional, isolada (implementada e em produção).
- **NfseSaaS.Web** — ASP.NET Core MVC + API, DI, middlewares, health check.
- **NfseSaaS.Tests** / **NfseSaaS.IntegrationTests** — xUnit.
- **tools/NfseSaaS.CertTool** — utilitário de linha de comando para inspeção/validação de certificado A1.
- **tools/NfseSaaS.EmitirTeste** — utilitário de linha de comando para emissão de teste contra a SEFIN.

## Multi-tenant

Toda entidade tenant-scoped implementa `ITenantEntity` (`Guid TenantId`).
O isolamento é **centralizado** no `AppDbContext` via *Global Query
Filters* aplicados por reflexão sobre todas as entidades que implementam
`ITenantEntity` — não é necessário (nem permitido) filtrar manualmente por
`TenantId` em uma consulta LINQ. No `SaveChanges`, o `TenantId` é forçado
em entidades novas (`Added`) e bloqueado contra alteração em entidades
existentes (`Modified`).

O `TenantId` da requisição atual é resolvido por `ICurrentTenant` /
`CurrentTenant`, **exclusivamente a partir de um Claim** do usuário
autenticado (`tenant_id`) — nunca de um valor recebido do navegador
(query string, header, body), evitando que um usuário do Tenant A acesse
dados do Tenant B alterando um valor no cliente.

A entidade `Tenant` é a única **global**: não implementa `ITenantEntity`
e não sofre o filtro (senão seria impossível localizar o próprio tenant).

Além do tenant, a maior parte das telas opera também no contexto de uma
**Empresa** selecionada (uma empresa pode ter múltiplos ambientes fiscais).
A seleção fica num único `<select>` no `_Layout.cshtml`, com a Empresa
atual persistida em cookie (`EmpresaAtualId`) — não em `sessionStorage`,
para que o badge da empresa já venha correto do servidor, sem flash.

## Integração fiscal (NfseSaaS.Nacional)

Módulo isolado (sem depender de Domain/EF Core/ASP.NET Core) responsável
por toda a comunicação com a SEFIN Nacional:

- Carregamento de certificado A1 (.pfx) por Empresa, via `HttpClient`
  nomeado (`ICertificateProvider` + `IHttpMessageHandlerBuilderFilter`) —
  cada Empresa autentica com seu próprio certificado em mTLS.
- `DpsBuilder` / `DpsSigner` — geração e assinatura digital da DPS (RSA-SHA1
  / XML-DSig conforme o padrão nacional).
- `NfseApiClient` — envio (GZip + Base64) e tratamento de resposta da SEFIN.
- `EventoCancelamentoBuilder` — geração/assinatura de eventos de cancelamento.
- `AdnDistribuicaoClient` — consulta ao Ambiente de Dados Nacional (sincronização de eventos).
- `DpsValidator` — validações fiscais antes do envio (evita erros de schema da SEFIN).

## Principais entidades e regras de negócio

- **Tenant**, **Empresa** (dados fiscais, ambiente Homologação/Produção),
  **Cliente** (CPF ou CNPJ), **Servico** (tributação + NBS).
- **Contrato** → **ContratoServico** (N linhas de serviço por contrato) →
  **ReajusteContrato** (histórico de reajuste por índice: IPCA, IGP-M,
  INCC, INPC, Selic, CDI ou Outro). **ContratoDocumento** para anexos
  (.pdf/.doc/.docx, isolados por tenant/empresa/contrato em disco).
- **Nfse** — snapshot fiscal completo no momento da emissão (nunca relê
  Servico/Empresa depois, para o histórico fiscal não mudar
  retroativamente); **NfseEvento** para o histórico de eventos
  (cancelamento, sincronização).
- **AuditLog** — trilha de auditoria das operações sensíveis.
- Exclusão real (`DELETE`) é bloqueada por FK (`RESTRICT`) quando há
  dependência real (ex.: Empresa com Cliente/Serviço/Nfse/Contrato
  vinculado); documentos e serviços de contrato usam `CASCADE` (são
  detalhe/anexo do próprio contrato).

## Perfis de acesso

`IdentityRole<Guid>` padrão, com os papéis: **Administrador**, **Emissor**,
**Financeiro**, **Consulta**, **Contador**. Emissão de nota exige
Administrador ou Emissor; cancelamento e CRUD de Empresa/Cliente/Serviço e
convite de usuário exigem Administrador; leituras são abertas a qualquer
usuário autenticado do tenant.

## Nota Mensal (emissão em lote)

A partir da tela de Notas Fiscais, é possível emitir em lote todos os
Contratos com cobrança mensal ativos numa competência. O processamento é
**sequencial** (nunca paralelo — o certificado mTLS é compartilhado), com
`try/catch` por item — uma falha não aborta o lote. Idempotência dupla
(chave determinística + checagem de "já emitido" na listagem) evita
duplicar nota ao reprocessar.

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL 17](https://www.postgresql.org/) (via Docker, recomendado) ou instalação local
- Docker + Docker Compose (opcional, mas recomendado para o Postgres de desenvolvimento)
- Certificado digital A1 (.pfx) por Empresa, para emissão real contra a SEFIN Nacional

## Como subir o PostgreSQL (desenvolvimento)

```bash
docker compose up -d
```

Isso sobe um PostgreSQL 17 em `localhost:5432`, banco `nfse_saas`,
usuário `postgres`, com volume persistente. A senha usada é apenas para
desenvolvimento local (ver `docker-compose.yml`) — nunca reaproveitar em
produção.

## Como configurar a connection string (User Secrets)

A connection string **não fica no `appsettings.json`** (contém a senha do
banco). Configure via User Secrets, a partir da pasta `src/NfseSaaS.Web`:

```bash
cd src/NfseSaaS.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=nfse_saas;Username=postgres;Password=postgres_dev_only;SSL Mode=Disable"
```

> Em desenvolvimento local, `SSL Mode=Disable` é necessário na connection
> string.

## Como executar as migrations

Se necessário, instale a ferramenta `dotnet-ef` (uma vez, globalmente):

```bash
dotnet tool install --global dotnet-ef
```

A partir da raiz da solução:

```bash
dotnet ef database update --project src/NfseSaaS.Infrastructure --startup-project src/NfseSaaS.Web
```

> A pasta `Migrations/` (incluindo `AppDbContextModelSnapshot.cs` e os
> arquivos `*.Designer.cs`) é gerada pelo `dotnet ef` — não editar manualmente.

## Como executar a aplicação

```bash
dotnet run --project src/NfseSaaS.Web
```

Acesse:

- `https://localhost:7080/` — página inicial
- `https://localhost:7080/health` — health check (aplicação + PostgreSQL)
- `https://localhost:7080/swagger` — Swagger (ambiente Development)

## Como rodar os testes

```bash
dotnet test
```

## Estrutura da solução

```
NfseSaaS.sln
docker-compose.yml
.gitignore
README.md
src/
  NfseSaaS.Domain/
    Common/          (BaseEntity, ITenantEntity)
    Enums/           (NfseStatus, StatusContrato, SituacaoContrato, TipoAmbiente,
                       TipoCobrancaContrato, IndiceReajusteContrato, NfseEventoTipo)
    Entities/        (Tenant, Empresa, Cliente, Servico, Contrato, ContratoServico,
                       ContratoDocumento, ReajusteContrato, Nfse, NfseEvento,
                       ContadorDps, AuditLog)
  NfseSaaS.Application/
    Abstractions/     (ICurrentTenant)
    UseCases/         (AuditLogs, Certificados, Clientes, Consultas, Contratos,
                        DocumentosContrato, Empresas, Exportacoes, HistoricoContrato,
                        Nfse, NfseEventos, NotaMensal, ReajustesContrato, Servicos,
                        SincronizacaoSefin)
    DependencyInjection.cs
  NfseSaaS.Infrastructure/
    Persistence/       (AppDbContext, Configurations/, Migrations/)
    MultiTenancy/       (CurrentTenant)
    Identity/            (ApplicationUser)
    DependencyInjection.cs
  NfseSaaS.Nacional/
    Options/            (NfseNacionalOptions, AdnOptions)
    Abstractions/       (ICertificateProvider)
    Models/ Responses/  (DpsRequest, EventoCancelamentoRequest, NfseNacionalResponse,
                          EventoNacionalResponse, DfeLoteResponse)
    Builders/           (DpsBuilder, EventoCancelamentoBuilder)
    Signing/            (DpsSigner)
    Validation/         (DpsValidator)
    Clients/            (NfseApiClient, AdnDistribuicaoClient, NfseResponseParser,
                          EventoResponseParser, CertificateHttpMessageHandlerBuilderFilter)
    Helpers/            (GZipHelper, XmlSerializationHelper, NfseXmlValoresParser)
    Exceptions/         (NfseValidationException, NfseCertificateException, NfseApiException)
    Services/           (NfseNacionalService)
    DependencyInjection.cs
  NfseSaaS.Web/
    Controllers/         (MVC: Empresas, Clientes, Servicos, Contratos, Nfse, Usuarios,
                           AuditLogs, Account, Home)
    Controllers/Api/      (Api: Empresas, Clientes, Servicos, Contratos, Nfse, NotaMensal,
                            Consultas, Exportacoes, AuditLogs, Auth)
    Views/
    Middleware/          (CorrelationId, ExceptionHandling)
    HealthChecks/        (PostgresHealthCheck)
    Program.cs
tests/
  NfseSaaS.Tests/
  NfseSaaS.IntegrationTests/
tools/
  NfseSaaS.CertTool/       (inspeção/validação de certificado A1)
  NfseSaaS.EmitirTeste/    (emissão de teste contra a SEFIN)
```

## O que fica para a Fase 8 (adiado conscientemente)

- Dashboard / MRR.
- Webhooks.
- API pública.
- Portal do cliente.
- Cliente estrangeiro, `UsuarioEmpresa`, Plano/Assinatura.

Nenhum desses itens tem demanda real confirmada ainda — entram apenas
quando houver necessidade concreta de negócio.
