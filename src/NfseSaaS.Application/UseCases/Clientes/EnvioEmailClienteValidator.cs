using FluentValidation;

namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>Campos do envio da nota por e-mail, comuns ao cadastro e à edição do cliente.</summary>
public interface IDadosEnvioEmailCliente
{
    string? Email { get; }
    bool EnviarNfsePorEmail { get; }
    IReadOnlyList<string>? EmailsCopia { get; }
    bool CopiaOculta { get; }
}

/// <summary>
/// Envio ligado exige o e-mail principal (não dá para mandar a nota para
/// ninguém). Cópias: até 5, cada uma um e-mail válido, sem repetir o
/// principal. Cópias ficam guardadas mesmo com o envio desligado, então o
/// formato é conferido sempre.
/// </summary>
public sealed class EnvioEmailClienteValidator : AbstractValidator<IDadosEnvioEmailCliente>
{
    public EnvioEmailClienteValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Informe o e-mail do cliente para enviar a nota por e-mail (ou desligue o envio).")
            .When(x => x.EnviarNfsePorEmail);

        RuleFor(x => EmailsCopiaCliente.Normalizar(x.EmailsCopia))
            .Must(l => l.Count <= EmailsCopiaCliente.Maximo)
            .WithName("EmailsCopia")
            .WithMessage($"Informe no máximo {EmailsCopiaCliente.Maximo} e-mails em cópia.");

        // Linha em branco é ignorada (Normalizar descarta); o resto é medido já aparado.
        RuleForEach(x => x.EmailsCopia)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Must(e => e.Trim().Length <= EmailsCopiaCliente.TamanhoMaximoEndereco)
            .WithMessage($"Cada e-mail em cópia deve ter no máximo {EmailsCopiaCliente.TamanhoMaximoEndereco} caracteres.")
            .Must(e => EmailValido(e.Trim()))
            .WithMessage("E-mail em cópia inválido: '{PropertyValue}'.");

        RuleFor(x => x)
            .Must(x => !EmailsCopiaCliente.Normalizar(x.EmailsCopia)
                .Contains(x.Email?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .OverridePropertyName("EmailsCopia")
            .WithMessage("O e-mail principal não precisa estar também nas cópias.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }

    // Mesmo critério do EmailAddress() do FluentValidation (um "@" com
    // texto antes e depois), sem espaço no meio.
    private static bool EmailValido(string e)
    {
        var arroba = e.IndexOf('@');
        return arroba > 0 && arroba == e.LastIndexOf('@') && arroba < e.Length - 1 && !e.Any(char.IsWhiteSpace);
    }
}
