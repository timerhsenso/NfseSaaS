using FluentValidation;
using NfseSaaS.Domain.Entities;
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

        // Medido no texto já aparado — o use case grava com Trim().
        RuleFor(x => x.Apelido)
            .NotEmpty().WithMessage("Apelido é obrigatório.")
            .Must(a => a is null || a.Trim().Length <= Cliente.TamanhoMaximoApelido)
            .WithMessage($"Apelido deve ter no máximo {Cliente.TamanhoMaximoApelido} caracteres.");

        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Telefone).MaximumLength(20);
        Include(new EnvioEmailClienteValidator());

        // Endereço (C3): em branco é aceito; preenchido, completo e no formato.
        EnderecoCliente.AplicarRegras(this, x => x.CodigoMunicipio, x => x.Cep, x => x.Logradouro, x => x.Numero,
            x => x.Complemento, x => x.Bairro, x => x.Uf);

        // Obrigatório para CNPJ e para quem retém o ISS (SEFIN E0235/E0237).
        RuleFor(x => x)
            .Must(x => EnderecoCliente.MotivoObrigatorio(x.CpfCnpj, x.RetemIss) is null
                       || !EnderecoCliente.Vazio(x.CodigoMunicipio, x.Cep, x.Logradouro, x.Numero, x.Bairro, x.Uf, x.Complemento))
            .WithName("Endereco")
            .WithMessage(x => EnderecoCliente.MotivoObrigatorio(x.CpfCnpj, x.RetemIss)!);
    }
}
