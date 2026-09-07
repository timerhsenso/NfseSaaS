using FluentValidation;

namespace NfseSaaS.Application.UseCases.Empresas;

/// <summary>Mesmas regras do cadastro, exceto Cnpj (não editável, nem faz parte deste DTO).</summary>
public sealed class AtualizarEmpresaRequestValidator : AbstractValidator<AtualizarEmpresaRequest>
{
    private static readonly string[] OpcoesSimplesNacional = { "1", "2", "3" };

    public AtualizarEmpresaRequestValidator()
    {
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

        RuleFor(x => x.PercentualTotalTributosSimplesNacional)
            .NotEmpty()
            .Matches(@"^\d+(\.\d{1,4})?$").WithMessage("PercentualTotalTributosSimplesNacional deve ser um número decimal (ex.: '3.00').")
            .When(x => x.OpSimpNac == "3");
    }
}
