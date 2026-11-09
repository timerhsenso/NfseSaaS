using FluentValidation;

namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>
/// Endereço do tomador (C3). No XSD da DPS o grupo toma/end é opcional, mas
/// a SEFIN exige o endereço nacional em três casos:
/// - tomador com CNPJ (E0235);
/// - ISS retido pelo tomador (E0237/E0241);
/// - nota com o grupo IBS/CBS, cuja incidência é o domicílio do tomador (E0239).
/// Fora disso (pessoa física, sem retenção, sem IBS/CBS) o endereço pode ficar
/// em branco. É tudo ou nada: preenchido pela metade não vale.
/// </summary>
public static class EnderecoCliente
{
    public const string MotivoCnpj = "Cliente com CNPJ precisa de endereço (exigência da SEFIN).";
    public const string MotivoRetencao = "Cliente que retém o ISS precisa de endereço (exigência da SEFIN).";
    public const string MotivoIbsCbs = "Nota com IBS/CBS precisa do endereço do cliente (exigência da SEFIN).";
    public const string MotivoIncompleto = "Endereço incompleto: preencha CEP, município, logradouro, número, bairro e UF, ou deixe todos em branco.";

    /// <summary>Nenhum dos campos do endereço preenchido.</summary>
    public static bool Vazio(string? codigoMunicipio, string? cep, string? logradouro, string? numero, string? bairro, string? uf = null, string? complemento = null) =>
        new[] { codigoMunicipio, cep, logradouro, numero, bairro, uf, complemento }.All(string.IsNullOrWhiteSpace);

    /// <summary>Os campos que a DPS exige no grupo end (cMun, CEP, xLgr, nro, xBairro) estão todos preenchidos.</summary>
    public static bool Completo(string? codigoMunicipio, string? cep, string? logradouro, string? numero, string? bairro) =>
        new[] { codigoMunicipio, cep, logradouro, numero, bairro }.All(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>CPF tem 11 caracteres; o resto (CNPJ numérico ou alfanumérico) é empresa.</summary>
    public static bool EhCnpj(string? cpfCnpj) =>
        (cpfCnpj ?? string.Empty).Count(char.IsLetterOrDigit) != 11;

    /// <summary>Por que este cliente precisa de endereço (nulo = pode ficar em branco).</summary>
    public static string? MotivoObrigatorio(string? cpfCnpj, bool retemIss, bool ibsCbs = false)
    {
        if (EhCnpj(cpfCnpj))
            return MotivoCnpj;
        if (retemIss)
            return MotivoRetencao;
        if (ibsCbs)
            return MotivoIbsCbs;
        return null;
    }

    /// <summary>Texto gravado no banco: aparado, e vazio quando não informado.</summary>
    public static string Normalizar(string? valor) => (valor ?? string.Empty).Trim();

    /// <summary>
    /// Regras de formato do endereço para os validadores de cadastro e de
    /// edição: em branco passa; preenchido (qualquer campo), todos os
    /// obrigatórios da DPS e o formato de cada um.
    /// </summary>
    internal static void AplicarRegras<T>(AbstractValidator<T> v,
        Func<T, string?> municipio, Func<T, string?> cep, Func<T, string?> logradouro, Func<T, string?> numero,
        Func<T, string?> complemento, Func<T, string?> bairro, Func<T, string?> uf)
    {
        bool Preenchido(T x) => !Vazio(municipio(x), cep(x), logradouro(x), numero(x), bairro(x), uf(x), complemento(x));

        v.RuleFor(x => x).Must(x => !Preenchido(x) || (Completo(municipio(x), cep(x), logradouro(x), numero(x), bairro(x)) && !string.IsNullOrWhiteSpace(uf(x))))
            .WithName("Endereco").WithMessage(MotivoIncompleto);

        v.RuleFor(x => municipio(x)).Matches(@"^\d{7}$").When(x => !string.IsNullOrWhiteSpace(municipio(x)))
            .WithName("CodigoMunicipio").WithMessage("CodigoMunicipio deve ser o código IBGE com 7 dígitos.");

        // Coluna 'cep' no banco é HasMaxLength(8) — só dígitos, sem hífen.
        v.RuleFor(x => cep(x)).Matches(@"^\d{8}$").When(x => !string.IsNullOrWhiteSpace(cep(x)))
            .WithName("Cep").WithMessage("Cep deve ter exatamente 8 dígitos, sem hífen (ex.: 40010000).");

        v.RuleFor(x => logradouro(x)).MaximumLength(200).WithName("Logradouro");
        v.RuleFor(x => numero(x)).MaximumLength(20).WithName("Numero");
        v.RuleFor(x => complemento(x)).MaximumLength(100).WithName("Complemento");
        v.RuleFor(x => bairro(x)).MaximumLength(100).WithName("Bairro");

        v.RuleFor(x => uf(x)).Matches(@"^[A-Z]{2}$").When(x => !string.IsNullOrWhiteSpace(uf(x)))
            .WithName("Uf").WithMessage("Uf deve ter exatamente 2 letras maiúsculas (ex.: 'BA').");
    }
}
