using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NfseSaaS.Infrastructure.Certificates;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// SEG1: passa os arquivos de certificado A1 da versão 1 (senha cifrada com
/// a mesma finalidade do .pfx) para a versão 2 (finalidades separadas), na
/// subida do sistema. Usa CertificateFileStore.ReprotegerAsync: confere que
/// o conteúdo novo abre igual ao antigo e troca o arquivo de forma atômica.
/// Idempotente (só mexe em versão 1). Falha numa Empresa não impede a
/// subida nem as outras: fica no log e a emissão continua lendo a versão 1.
/// </summary>
public static class CertificadoVersaoSeeder
{
    public static async Task AtualizarAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<CertificateFileStore>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CertificadoVersaoSeeder));

        var atualizados = 0;
        foreach (var empresaId in store.ListarEmpresasComCertificado())
        {
            try
            {
                if (await store.VersaoArquivoAsync(empresaId, cancellationToken) >= CertificateFileStore.VersaoAtualArquivo)
                    continue;

                await store.ReprotegerAsync(empresaId, cancellationToken);
                atualizados++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Certificado da empresa {EmpresaId} não passou para a versão {Versao} do arquivo; continua na versão antiga.",
                    empresaId, CertificateFileStore.VersaoAtualArquivo);
            }
        }

        if (atualizados > 0)
            logger.LogInformation("{Quantidade} certificado(s) A1 passaram para a versão {Versao} do arquivo (senha com finalidade própria).",
                atualizados, CertificateFileStore.VersaoAtualArquivo);
    }
}
