namespace NfseSaaS.Application.UseCases.Plataforma;

/// <summary>
/// Situação das chaves do Data Protection (abrem os certificados A1 de
/// todos os clientes e assinam o cookie de login), vista pelo operador.
/// </summary>
/// <param name="Pasta">Pasta das chaves no servidor (vazio = pasta padrão do ASP.NET).</param>
/// <param name="ChavesCriptografadas">Há certificado (ou DPAPI) configurado: chaves NOVAS já nascem criptografadas.</param>
/// <param name="Mecanismo">Como as chaves novas são criptografadas ("certificado {thumbprint}", "DPAPI (máquina)" ou "nenhum").</param>
/// <param name="ChavesTotal">Arquivos de chave na pasta.</param>
/// <param name="ChavesSemCriptografia">Chaves gravadas em texto aberto (anteriores à configuração do certificado).</param>
/// <param name="Certificados">Certificados A1 de Empresas guardados no servidor.</param>
/// <param name="PodeReproteger">Dá para rodar "Proteger chaves antigas" agora.</param>
/// <param name="Orientacao">O que fazer, em português simples.</param>
public sealed record ProtecaoChavesResponse(
    string Pasta,
    bool ChavesCriptografadas,
    string Mecanismo,
    int ChavesTotal,
    int ChavesSemCriptografia,
    int Certificados,
    bool PodeReproteger,
    string Orientacao);

/// <summary>Resultado de "Proteger chaves antigas".</summary>
public sealed record ReprotecaoChavesResponse(int CertificadosReprotegidos, int ChavesRemovidas, Guid ChaveNova);

/// <summary>Situação das chaves do Data Protection — só o operador da plataforma chama.</summary>
public interface IConsultarProtecaoChavesUseCase
{
    Task<ProtecaoChavesResponse> ExecutarAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Cria uma chave nova (já criptografada), regrava todos os certificados com
/// ela e só então apaga as chaves antigas sem criptografia. Só o operador da
/// plataforma chama.
/// </summary>
public interface IReprotegerChavesUseCase
{
    Task<ReprotecaoChavesResponse> ExecutarAsync(Guid? operadorId, CancellationToken cancellationToken);
}
