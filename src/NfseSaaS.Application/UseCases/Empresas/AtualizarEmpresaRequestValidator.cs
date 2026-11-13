using FluentValidation;

namespace NfseSaaS.Application.UseCases.Empresas;

/// <summary>Mesmas regras do cadastro, exceto Cnpj (não editável, nem faz parte deste DTO).</summary>
public sealed class AtualizarEmpresaRequestValidator : AbstractValidator<AtualizarEmpresaRequest>
{
    public AtualizarEmpresaRequestValidator()
    {
        RuleFor(x => x.RazaoSocial).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NomeFantasia).MaximumLength(200);

        RuleFor(x => x.InscricaoMunicipal).NotEmpty().MaximumLength(30);

        RuleFor(x => x.CodigoMunicipio)
            .NotEmpty()
            .Matches(@"^\d{7}$").WithMessage("CodigoMunicipio deve ser o código IBGE com 7 dígitos.");

        // A DPS exige 6 a 20 dígitos (TSTelefone). Máscara é aceita na
        // digitação — o DpsBuilder manda só os dígitos.
        RuleFor(x => x.Telefone)
            .NotEmpty()
            .MaximumLength(20)
            .Must(t => t.Count(char.IsDigit) is >= 6 and <= 20)
            .WithMessage("Telefone deve ter entre 6 e 20 dígitos (DDD + número).");
        RuleFor(x => x.Email).NotEmpty().EmailAddress();

        RuleFor(x => x.OpSimpNac)
            .NotEmpty()
            .Must(v => CodigosFiscaisEmpresa.OpcoesSimplesNacional.Contains(v))
            .WithMessage("OpSimpNac deve ser '1' (Não optante), '2' (Optante MEI) ou '3' (Optante Simples Nacional exceto MEI).");

        RuleFor(x => x.RegApTribSN).NotEmpty();
        RuleFor(x => x.RegEspTrib)
            .NotEmpty()
            .Must(v => CodigosFiscaisEmpresa.RegimesEspeciais.Contains(v))
            .WithMessage("RegEspTrib deve ser um dos códigos do XSD da SEFIN: 0, 1, 2, 3, 4, 5, 6 ou 9.");
        RuleFor(x => x.TribIssqn)
            .NotEmpty()
            .Must(v => CodigosFiscaisEmpresa.TributacoesIssqn.Contains(v))
            .WithMessage("Por enquanto só é suportada a tributação \"Operação tributável\" (imunidade, exportação e não incidência ainda não são emitidas por este sistema).");
        RuleFor(x => x.AliquotaIss)
            .InclusiveBetween(CodigosFiscaisEmpresa.AliquotaIssMinima, CodigosFiscaisEmpresa.AliquotaIssMaxima)
            .When(x => x.AliquotaIss.HasValue)
            .WithMessage("Alíquota de ISS deve estar entre 0,01% e 5,00%.");
        RuleFor(x => x.CstPisCofins)
            .NotEmpty()
            .Must(v => CodigosFiscaisEmpresa.CstsPisCofins.Contains(v))
            .WithMessage("CstPisCofins deve ser um CST de saída válido: 00 a 09, 49 ou 99.");

        // Mesma regra do DpsValidator (módulo Nacional) — ver comentário
        // equivalente em CadastrarEmpresaRequestValidator.
        RuleFor(x => x.PercentualTotalTributosSimplesNacional)
            .NotEmpty()
            .Matches(@"^\d+(\.\d{1,4})?$").WithMessage("PercentualTotalTributosSimplesNacional deve ser um número decimal (ex.: '3.00').")
            .When(x => x.OpSimpNac is "2" or "3");

        RuleFor(x => x.DiasAlertaReajusteContratoPadrao).GreaterThan(0);
    }
}
