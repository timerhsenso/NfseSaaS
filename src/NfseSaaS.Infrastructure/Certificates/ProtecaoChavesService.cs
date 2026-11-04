using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Plataforma;

namespace NfseSaaS.Infrastructure.Certificates;

/// <summary>
/// Consulta e "Proteger chaves antigas" das chaves do Data Protection.
///
/// Ligar o certificado de proteção (DataProtection:CertificadoPfxPath) só
/// criptografa as chaves NOVAS. As antigas continuam em texto aberto na
/// pasta, e os certificados A1 já enviados dependem delas. Para tirá-las do
/// disco sem perder nenhum certificado, a ordem é:
///  1. criar uma chave nova (já nasce criptografada) e conferir no arquivo;
///  2. regravar todos os certificados com ela (CertificateFileStore.ReprotegerAsync);
///  3. conferir que nenhum certificado ainda usa uma chave antiga;
///  4. só então apagar os arquivos das chaves antigas.
/// Se qualquer passo falhar, nada é apagado e dá para rodar de novo.
///
/// Efeito colateral esperado: cookies de login e links de "esqueci a
/// senha" assinados com a chave antiga deixam de valer na próxima vez que o
/// sistema reiniciar (todo mundo entra de novo). Nenhum dado se perde.
/// </summary>
public sealed class ProtecaoChavesService : IConsultarProtecaoChavesUseCase, IReprotegerChavesUseCase
{
    private const int DiasValidadeChave = 90;

    // Uma reproteção por vez no processo (o botão pode ser clicado duas vezes).
    private static readonly SemaphoreSlim Trava = new(1, 1);

    private readonly ProtecaoChavesStatus _status;
    private readonly IKeyManager _keyManager;
    private readonly CertificateFileStore _certificados;
    private readonly ILogger<ProtecaoChavesService> _logger;

    public ProtecaoChavesService(ProtecaoChavesStatus status, IKeyManager keyManager, CertificateFileStore certificados, ILogger<ProtecaoChavesService> logger)
    {
        _status = status;
        _keyManager = keyManager;
        _certificados = certificados;
        _logger = logger;
    }

    Task<ProtecaoChavesResponse> IConsultarProtecaoChavesUseCase.ExecutarAsync(CancellationToken cancellationToken)
        => Task.FromResult(Consultar());

    public ProtecaoChavesResponse Consultar()
    {
        var chaves = LerArquivosDeChave();
        var semCriptografia = chaves.Count(c => !c.Criptografada);
        var certificados = _certificados.ListarEmpresasComCertificado().Count;
        var pode = PodeReproteger(semCriptografia);

        string orientacao;
        if (string.IsNullOrWhiteSpace(_status.CaminhoChaves))
            orientacao = "As chaves estão na pasta padrão do ASP.NET. Defina DataProtection:KeysPath para poder conferir e proteger as chaves por aqui.";
        else if (!_status.ChavesCriptografadas)
            orientacao = "Falta configurar o certificado de proteção (DataProtection__CertificadoPfxPath e DataProtection__CertificadoSenha). Veja docs/operacao/protecao-chaves.md.";
        else if (semCriptografia > 0)
            orientacao = $"{semCriptografia} chave(s) antiga(s) ainda está(ão) sem criptografia. Rode um backup manual e clique em \"Proteger chaves antigas\".";
        else
            orientacao = "Todas as chaves estão criptografadas. Nada a fazer.";

        return new ProtecaoChavesResponse(
            _status.CaminhoChaves, _status.ChavesCriptografadas, _status.MecanismoCriptografia,
            chaves.Count, semCriptografia, certificados, pode, orientacao);
    }

    async Task<ReprotecaoChavesResponse> IReprotegerChavesUseCase.ExecutarAsync(Guid? operadorId, CancellationToken cancellationToken)
        => await ReprotegerAsync(operadorId, cancellationToken);

    public async Task<ReprotecaoChavesResponse> ReprotegerAsync(Guid? operadorId, CancellationToken cancellationToken)
    {
        await Trava.WaitAsync(cancellationToken);
        try
        {
            var antigas = LerArquivosDeChave().Where(c => !c.Criptografada).ToList();
            if (!PodeReproteger(antigas.Count))
                throw new RegraNegocioException(Consultar().Orientacao);

            var idsAntigos = antigas.Select(c => c.Id).ToHashSet();

            // 1. Chave nova. Com o certificado configurado ela já é gravada
            // criptografada; o CreateNewKey também invalida o cache do
            // "key ring", então o próximo Protect já usa esta chave.
            var agora = DateTimeOffset.UtcNow;
            var nova = _keyManager.CreateNewKey(agora, agora.AddDays(DiasValidadeChave));

            var arquivoNovo = LerArquivosDeChave().FirstOrDefault(c => c.Id == nova.KeyId);
            if (arquivoNovo is null || !arquivoNovo.Criptografada)
                throw new InvalidOperationException(
                    $"A chave nova {nova.KeyId} não foi gravada criptografada em {_status.CaminhoChaves}. Nada foi apagado.");

            // 2. Regravar os certificados com a chave nova.
            var empresas = _certificados.ListarEmpresasComCertificado();
            foreach (var empresaId in empresas)
                await _certificados.ReprotegerAsync(empresaId, cancellationToken);

            // 3. Conferir: nenhum certificado pode continuar numa chave antiga.
            foreach (var empresaId in empresas)
            {
                var usadas = await _certificados.ChavesUsadasAsync(empresaId, cancellationToken);
                if (usadas.Overlaps(idsAntigos))
                    throw new InvalidOperationException(
                        $"O certificado da empresa {empresaId} continua usando uma chave antiga depois de regravado. Nada foi apagado; rode de novo.");
            }

            // 4. Apagar os arquivos das chaves antigas (texto aberto).
            foreach (var antiga in antigas)
                File.Delete(antiga.Caminho);

            _logger.LogWarning(
                "Chaves do Data Protection reprotegidas pelo operador {OperadorId}: chave nova {ChaveNova}, {Certificados} certificado(s) regravado(s), {Removidas} chave(s) antiga(s) sem criptografia apagada(s): {Ids}",
                operadorId, nova.KeyId, empresas.Count, antigas.Count, string.Join(", ", idsAntigos));

            return new ReprotecaoChavesResponse(empresas.Count, antigas.Count, nova.KeyId);
        }
        finally
        {
            Trava.Release();
        }
    }

    private bool PodeReproteger(int chavesSemCriptografia) =>
        !string.IsNullOrWhiteSpace(_status.CaminhoChaves) && _status.ChavesCriptografadas && chavesSemCriptografia > 0;

    /// <summary>
    /// Arquivos "key-{id}.xml" da pasta. Chave criptografada tem o elemento
    /// &lt;encryptedSecret&gt;; em texto aberto, o &lt;masterKey&gt; aparece direto.
    /// </summary>
    private List<ArquivoChave> LerArquivosDeChave()
    {
        if (string.IsNullOrWhiteSpace(_status.CaminhoChaves) || !Directory.Exists(_status.CaminhoChaves))
            return new List<ArquivoChave>();

        var lista = new List<ArquivoChave>();
        foreach (var caminho in Directory.EnumerateFiles(_status.CaminhoChaves, "key-*.xml"))
        {
            var raiz = XDocument.Load(caminho).Root;
            if (raiz is null || !Guid.TryParse(raiz.Attribute("id")?.Value, out var id))
                continue;

            var criptografada = raiz.Descendants().Any(e => e.Name.LocalName == "encryptedSecret");
            lista.Add(new ArquivoChave(id, caminho, criptografada));
        }

        return lista;
    }

    private sealed record ArquivoChave(Guid Id, string Caminho, bool Criptografada);
}
