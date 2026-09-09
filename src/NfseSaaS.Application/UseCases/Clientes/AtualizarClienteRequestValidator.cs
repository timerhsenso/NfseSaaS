using FluentValidation;

namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>Mesmas regras do cadastro, exceto CpfCnpj (não editável, nem faz parte deste DTO).</summary>
public sealed class AtualizarClienteRequestValidator : AbstractValidator<AtualizarClienteRequest>
{
    public AtualizarClienteRequestValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Telefone).MaximumLength(20);

        RuleFor(x => x.CodigoMunicipio)
            .NotEmpty()
            .Matches(@"^\d{7}$").WithMessage("CodigoMunicipio deve ser o código IBGE com 7 dígitos.");

        // Coluna 'cep' no banco é HasMaxLength(8) — só dígitos, sem hífen.
        RuleFor(x => x.Cep)
            .NotEmpty()
            .Matches(@"^\d{8}$").WithMessage("Cep deve ter exatamente 8 dígitos, sem hífen (ex.: 40010000).");

        RuleFor(x => x.Logradouro).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Numero).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Complemento).MaximumLength(100);
        RuleFor(x => x.Bairro).NotEmpty().MaximumLength(100);

        RuleFor(x => x.Uf)
            .NotEmpty()
            .Matches(@"^[A-Z]{2}$").WithMessage("Uf deve ter exatamente 2 letras maiúsculas (ex.: 'BA').");
    }
}
