using FluentValidation;
using NfseSaaS.Application.Validation;

namespace NfseSaaS.Application.UseCases.Clientes;

public sealed class CadastrarClienteRequestValidator : AbstractValidator<CadastrarClienteRequest>
{
    public CadastrarClienteRequestValidator()
    {
        RuleFor(x => x.EmpresaId).NotEmpty();

        RuleFor(x => x.CpfCnpj)
            .NotEmpty()
            .Must(DocumentoFiscalValidator.CpfOuCnpjValido).WithMessage("CpfCnpj inválido (dígito verificador não confere).");

        RuleFor(x => x.Nome).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Telefone).MaximumLength(20);

        RuleFor(x => x.CodigoMunicipio)
            .NotEmpty()
            .Matches(@"^\d{7}$").WithMessage("CodigoMunicipio deve ser o código IBGE com 7 dígitos.");

        // Coluna 'cep' no banco é HasMaxLength(8) — só dígitos, sem hífen.
        // Se algum dia quiser aceitar "00000-000" na entrada, normalize
        // (remova o hífen) ANTES de persistir, não aqui na validação.
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
