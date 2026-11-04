namespace NfseSaaS.Application.UseCases.Certificados;

/// <summary>
/// Faixas do aviso de certificado digital vencendo: 30, 15 e 7 dias antes
/// e depois de vencido. Cada faixa sai UMA vez por certificado (a validade
/// faz parte da chave do aviso — renovou, as faixas recomeçam).
/// </summary>
public static class FaixaAvisoCertificado
{
    public const int Vencido = 0;

    /// <summary>
    /// Faixa em que o certificado está, ou null se ainda falta mais de 30
    /// dias. Se o job ficar um dia sem rodar, o aviso sai no dia seguinte
    /// (é por faixa, não por "exatamente 30 dias").
    /// </summary>
    public static int? Calcular(int diasRestantes) => diasRestantes switch
    {
        < 0 => Vencido,
        <= 7 => 7,
        <= 15 => 15,
        <= 30 => 30,
        _ => null
    };
}

/// <summary>Resultado do aviso de uma Empresa (para log do job).</summary>
public sealed record AvisoCertificadoResult(bool Enviado, int? DiasRestantes, int? Faixa, IReadOnlyList<string> Destinatarios, string? Motivo);

/// <summary>
/// Confere o certificado digital de UMA Empresa (Tenant já definido no
/// escopo) e, se ele entrou numa faixa de aviso ainda não avisada, manda
/// e-mail aos administradores da conta e ao e-mail da própria Empresa.
/// </summary>
public interface IAvisarCertificadoVencendoUseCase
{
    Task<AvisoCertificadoResult> ExecutarAsync(Guid empresaId, DateOnly hoje, CancellationToken cancellationToken);
}
