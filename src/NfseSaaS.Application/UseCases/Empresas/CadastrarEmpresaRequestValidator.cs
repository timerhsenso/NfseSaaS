using FluentValidation;
using NfseSaaS.Application.Validation;

namespace NfseSaaS.Application.UseCases.Empresas;

public sealed class CadastrarEmpresaRequestValidator : AbstractValidator<CadastrarEmpresaRequest>
{
    private static readonly string[] OpcoesSimplesNacional = { "1", "2", "3" };

    public CadastrarEmpresaRequestValidator()
    {
        RuleFor(x => x.Cnpj)
            .NotEmpty()
            .Must(DocumentoFiscalValidator.CnpjValido).WithMessage("Cnpj inválido (dígito verificador não confere).");

        RuleFor(x => x.RazaoSocial).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NomeFantasia).MaximumLength(200);

        RuleFor(x => x.InscricaoMunicipal).NotEmpty().MaximumLength(30);

        RuleFor(x => x.CodigoMunicipio)
            .NotEmpty()
            .Matches(@"^\d{7}$").WithMessage("CodigoMunicipio deve ser o código IBGE com 7 dígitos.");

        RuleFor(x => x.Telefone).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();

        RuleFor(x => x.OpSimpNac)
            .NotEmpty()
            .Must(v => OpcoesSimplesNacional.Contains(v))
            .WithMessage("OpSimpNac deve ser '1' (Não optante), '2' (Optante MEI) ou '3' (Optante Simples Nacional exceto MEI).");

        RuleFor(x => x.RegApTribSN).NotEmpty();
        RuleFor(x => x.RegEspTrib).NotEmpty();
        RuleFor(x => x.TribIssqn).NotEmpty();
        RuleFor(x => x.TpRetIssqn).NotEmpty();
        RuleFor(x => x.CstPisCofins).NotEmpty();
        RuleFor(x => x.TpRetPisCofins).NotEmpty();

        // Só é obrigatório para quem optou pelo Simples Nacional (exceto MEI) —
        // é o campo pTotTribSN da DPS, não faz sentido fora desse regime.
        RuleFor(x => x.PercentualTotalTributosSimplesNacional)
            .NotEmpty()
            .Matches(@"^\d+(\.\d{1,4})?$").WithMessage("PercentualTotalTributosSimplesNacional deve ser um número decimal (ex.: '3.00').")
            .When(x => x.OpSimpNac == "3");
    }
}
