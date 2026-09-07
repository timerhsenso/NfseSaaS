using System.Text.RegularExpressions;

namespace NfseSaaS.Application.Validation;

/// <summary>
/// Validação de CPF/CNPJ por dígito verificador (módulo 11) — não é uma
/// checagem de formato/tamanho superficial, calcula os dígitos de fato.
///
/// CNPJ alfanumérico (Instrução Normativa RFB 2.229/2024, em vigor desde
/// 31/07/2026 — primeiro CNPJ com letra já emitido pela Receita Federal):
/// as 12 primeiras posições podem ser dígito (0-9) OU letra maiúscula
/// (A-Z); as 2 últimas (dígitos verificadores) continuam sempre
/// numéricas. O cálculo do DV continua módulo 11 — cada caractere é
/// convertido pelo valor ASCII menos 48 antes de multiplicar pelos pesos
/// (dígitos '0'-'9' já valem 0-9 nessa conversão, então CNPJs só-numéricos
/// — todos os já existentes — continuam validando exatamente como antes:
/// o algoritmo novo é retrocompatível por design). CPF não foi afetado por
/// essa mudança, continua sempre numérico.
/// </summary>
public static class DocumentoFiscalValidator
{
    private static readonly Regex FormatoCnpj = new(@"^[0-9A-Z]{12}[0-9]{2}$", RegexOptions.Compiled);

    public static bool CnpjValido(string? cnpj)
    {
        if (string.IsNullOrWhiteSpace(cnpj))
            return false;

        var caracteres = NormalizarAlfanumerico(cnpj);

        if (caracteres.Length != 14 || TodosCaracteresIguais(caracteres) || !FormatoCnpj.IsMatch(caracteres))
            return false;

        var multiplicador1 = new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var multiplicador2 = new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

        var primeiroDigito = CalcularDigitoVerificador(caracteres[..12], multiplicador1);
        var segundoDigito = CalcularDigitoVerificador(caracteres[..12] + primeiroDigito, multiplicador2);

        return caracteres.EndsWith($"{primeiroDigito}{segundoDigito}");
    }

    public static bool CpfValido(string? cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf))
            return false;

        var digitos = SomenteDigitos(cpf);

        if (digitos.Length != 11 || TodosCaracteresIguais(digitos))
            return false;

        var multiplicador1 = new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2 };
        var multiplicador2 = new[] { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 };

        var primeiroDigito = CalcularDigitoVerificador(digitos[..9], multiplicador1);
        var segundoDigito = CalcularDigitoVerificador(digitos[..9] + primeiroDigito, multiplicador2);

        return digitos.EndsWith($"{primeiroDigito}{segundoDigito}");
    }

    /// <summary>
    /// Aceita CPF (11 posições, só numérico) ou CNPJ (14 posições, podendo
    /// ter letra maiúscula nas 12 primeiras) — usado no campo CpfCnpj de
    /// Cliente, que aceita os dois documentos.
    /// </summary>
    public static bool CpfOuCnpjValido(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento))
            return false;

        var caracteres = NormalizarAlfanumerico(documento);

        return caracteres.Length switch
        {
            11 => CpfValido(documento),
            14 => CnpjValido(documento),
            _ => false
        };
    }

    private static int CalcularDigitoVerificador(string baseCaracteres, int[] multiplicadores)
    {
        var soma = 0;
        for (var i = 0; i < multiplicadores.Length; i++)
            soma += (baseCaracteres[i] - '0') * multiplicadores[i]; // valor ASCII - 48; retrocompatível com dígito puro

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    /// <summary>Remove pontuação (./-) e espaços, mantém dígitos e letras, letras em maiúsculo.</summary>
    private static string NormalizarAlfanumerico(string valor) =>
        new(valor.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string SomenteDigitos(string valor) => new(valor.Where(char.IsDigit).ToArray());

    private static bool TodosCaracteresIguais(string caracteres) => caracteres.Distinct().Count() == 1;
}
