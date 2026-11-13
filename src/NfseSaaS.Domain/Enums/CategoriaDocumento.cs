namespace NfseSaaS.Domain.Enums;

/// <summary>
/// DOC4: grupo do tipo de documento no catálogo (só para organizar a escolha
/// e a tela). Valor numérico gravado no banco: não renumerar.
/// </summary>
public enum CategoriaDocumento
{
    Fiscal = 1,
    Trabalhista = 2,
    /// <summary>Cadastro e sociedade: cartão CNPJ, contrato social, inscrições, alvarás.</summary>
    Cadastral = 3,
    FinanceiroContabil = 4,
    SegurancaTrabalho = 5,
    SaudeOcupacional = 6,
    /// <summary>Qualificação técnica: ART, registro no conselho, atestados, CNO.</summary>
    Tecnico = 7,
    AmbientalSanitario = 8,
    Juridico = 9,
    Outros = 99
}
