namespace NfseSaaS.Web.Services;

public sealed record VersaoAtual(string Versao, string Commit, DateTimeOffset? DeployEm);

/// <summary>
/// Versão/commit/data de deploy vêm de variáveis de ambiente
/// (APP_VERSION/APP_COMMIT/APP_DEPLOY_EM), setadas pelo próprio pipeline
/// de deploy no momento do "docker compose up" — não fazem parte do
/// código, então não tem nada pra "esquecer de atualizar". Em ambiente
/// local (sem essas variáveis), cai num fallback "dev" — deixa claro que
/// não é uma versão de produção rastreada.
///
/// O changelog em si (o "o que foi implementado") é o CHANGELOG.md do
/// repo — fonte da verdade única, editada por humano a cada release,
/// versionada junto com o código. Ver NfseSaaS.Web.csproj: o build
/// sempre copia esse arquivo pro lado do .dll, então ele existe no
/// runtime independente de como o Dockerfile foi escrito.
/// </summary>
public interface IVersaoInfo
{
    VersaoAtual ObterAtual();

    string ObterChangelogMarkdown();
}

public sealed class VersaoInfo : IVersaoInfo
{
    private readonly string _caminhoChangelog;

    public VersaoInfo(IHostEnvironment environment)
    {
        _caminhoChangelog = Path.Combine(environment.ContentRootPath, "CHANGELOG.md");
    }

    public VersaoAtual ObterAtual()
    {
        var versao = Environment.GetEnvironmentVariable("APP_VERSION");
        var commit = Environment.GetEnvironmentVariable("APP_COMMIT");
        var deployEmTexto = Environment.GetEnvironmentVariable("APP_DEPLOY_EM");

        DateTimeOffset? deployEm = DateTimeOffset.TryParse(deployEmTexto, out var parsed) ? parsed : null;

        return new VersaoAtual(
            string.IsNullOrWhiteSpace(versao) ? "dev (não rastreada)" : versao,
            string.IsNullOrWhiteSpace(commit) ? "—" : commit,
            deployEm);
    }

    public string ObterChangelogMarkdown() =>
        File.Exists(_caminhoChangelog)
            ? File.ReadAllText(_caminhoChangelog)
            : "CHANGELOG.md não encontrado no ambiente atual.";
}
