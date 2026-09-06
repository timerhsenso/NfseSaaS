# NfseSaaS

SaaS multiempresa (multi-tenant) para emissão de NFS-e através do Sistema
Nacional da NFS-e / SEFIN Nacional.

> **Status:** Fase 1 — fundação da arquitetura (compilável, organizada,
> sem regras fiscais reais ainda). A integração fiscal completa (assinatura
> da DPS e emissão real) será migrada da POC validada na Fase 2.

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
- **NfseSaaS.Nacional** — integração fiscal com a SEFIN Nacional, isolada.
- **NfseSaaS.Web** — ASP.NET Core MVC, DI, middlewares, health check.
- **NfseSaaS.Tests** / **NfseSaaS.IntegrationTests** — xUnit.

## Multi-tenant

Toda entidade tenant-scoped implementa `ITenantEntity` (`Guid TenantId`).
O isolamento é **centralizado** no `AppDbContext` via *Global Query
Filters* aplicados por reflexão sobre todas as entidades que implementam
`ITenantEntity` — não é necessário (nem permitido) filtrar manualmente por
`TenantId` em uma consulta LINQ.

O `TenantId` da requisição atual é resolvido por `ICurrentTenant` /
`CurrentTenant`, **exclusivamente a partir de um Claim** do usuário
autenticado (`tenant_id`) — nunca de um valor recebido do navegador
(query string, header, body), evitando que um usuário do Tenant A acesse
dados do Tenant B alterando um valor no cliente.

A entidade `Tenant` é a única **global**: não implementa `ITenantEntity`
e não sofre o filtro (senão seria impossível localizar o próprio tenant).

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL 17](https://www.postgresql.org/) (via Docker, recomendado) ou instalação local
- Docker + Docker Compose (opcional, mas recomendado para o Postgres de desenvolvimento)

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
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=nfse_saas;Username=postgres;Password=postgres_dev_only"
```

## Como executar as migrations

Se necessário, instale a ferramenta `dotnet-ef` (uma vez, globalmente):

```bash
dotnet tool install --global dotnet-ef
```

A partir da raiz da solução:

```bash
dotnet ef migrations add InitialCreate --project src/NfseSaaS.Infrastructure --startup-project src/NfseSaaS.Web

dotnet ef database update --project src/NfseSaaS.Infrastructure --startup-project src/NfseSaaS.Web
```

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
    Enums/           (NfseStatus)
    Entities/        (Tenant, Empresa, Cliente, Servico, Nfse, AuditLog)
  NfseSaaS.Application/
    Abstractions/     (ICurrentTenant)
    UseCases/         (Nfse, Clientes, Servicos — DTOs/interfaces, Fase 2)
    DependencyInjection.cs
  NfseSaaS.Infrastructure/
    Persistence/       (AppDbContext, Configurations/)
    MultiTenancy/       (CurrentTenant)
    Identity/            (ApplicationUser)
    DependencyInjection.cs
  NfseSaaS.Nacional/
    Options/           (NfseNacionalOptions)
    Abstractions/       (ICertificateProvider)
    Models/ Responses/  (DpsRequest, NfseNacionalResponse)
    Builders/ Signing/ Validation/  (interfaces + placeholders — migração Fase 2)
    Clients/            (NfseApiClient, NfseResponseParser — implementados)
    Helpers/            (GZipHelper — implementado)
    Services/           (NfseNacionalService)
    DependencyInjection.cs
  NfseSaaS.Web/
    Controllers/ Views/
    Middleware/         (CorrelationId, ExceptionHandling)
    HealthChecks/       (PostgresHealthCheck)
    Program.cs
tests/
  NfseSaaS.Tests/
  NfseSaaS.IntegrationTests/
```

## O que ficou deliberadamente para a Fase 2

- Migração real de `DpsBuilder`/`DpsSigner` da POC (`NfsePoc/DpsBuilder.cs`) para `NfseSaaS.Nacional`.
- Implementação de `ICertificateProvider` (origem real do certificado: arquivo seguro, Key Vault, etc.).
- Conexão do certificado ao `HttpClient` do `NfseApiClient` para mTLS real.
- Implementação dos casos de uso da Application (`EmitirNfseUseCase` etc.).
- Regras de validação fiscal reais em `DpsValidator` (faixas de série, CNPJ/CPF, código de tributação por município).
- Cobrança, dashboard, e-mail, WhatsApp, app mobile e demais itens fora de escopo desta fase (ver especificação).

## Decisões técnicas para você revisar

Ver seção correspondente na resposta que acompanha esta entrega.
