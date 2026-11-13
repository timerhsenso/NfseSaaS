using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Infrastructure.Persistence.Seeders;
using NfseSaaS.Infrastructure.UseCases;

namespace NfseSaaS.Tests;

/// <summary>DOC4: o catálogo com os 9 tipos de sistema (os mesmos da migration), para os testes das regras.</summary>
internal static class CatalogoDocumentosTeste
{
    public static readonly CatalogoDocumentosEmpresa Sistema =
        new(CatalogoDocumentosSeeder.Sistema().Select(CatalogoDocumentosService.Info));
}
