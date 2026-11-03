using FluentValidation;
using NfseSaaS.Application.Validation;

namespace NfseSaaS.Application.UseCases.Empresas;

public sealed class CadastrarEmpresaRequestValidator : AbstractValidator<CadastrarEmpresaRequest>
{
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
            .WithMessage("TribIssqn deve ser 1, 2, 3 ou 4 (XSD da SEFIN).");
        RuleFor(x => x.TpRetIssqn)
            .NotEmpty()
            .Must(v => CodigosFiscaisEmpresa.RetencoesIssqn.Contains(v))
            .WithMessage("TpRetIssqn deve ser 1, 2 ou 3 (XSD da SEFIN).");
        RuleFor(x => x.CstPisCofins)
            .NotEmpty()
            .Must(v => CodigosFiscaisEmpresa.CstsPisCofins.Contains(v))
            .WithMessage("CstPisCofins deve ser um CST de saída válido: 00 a 09, 49 ou 99.");
        RuleFor(x => x.TpRetPisCofins)
            .NotEmpty()
            .Must(v => CodigosFiscaisEmpresa.RetencoesPisCofins.Contains(v))
            .WithMessage("TpRetPisCofins deve ser de 0 a 9 (XSD da SEFIN).");

        // Mesma regra do DpsValidator (módulo Nacional): pTotTribSN é
        // exigido pela SEFIN tanto pra MEI ("2") quanto pra ME/EPP ("3"),
        // não só "3" — travar aqui, no cadastro, é o que impede a Empresa
        // de ficar salva em estado que só quebra depois, na hora de emitir
        // (era possível salvar Optante sem o percentual e só descobrir o
        // problema quando a SEFIN rejeitasse a DPS).
        RuleFor(x => x.PercentualTotalTributosSimplesNacional)
            .NotEmpty()
            .Matches(@"^\d+(\.\d{1,4})?$").WithMessage("PercentualTotalTributosSimplesNacional deve ser um número decimal (ex.: '3.00').")
            .When(x => x.OpSimpNac is "2" or "3");
    }
}
