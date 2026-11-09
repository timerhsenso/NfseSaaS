using FluentValidation;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>Mesmas regras do cadastro, exceto CpfCnpj (não editável, nem faz parte deste DTO).</summary>
public sealed class AtualizarClienteRequestValidator : AbstractValidator<AtualizarClienteRequest>
{
    public AtualizarClienteRequestValidator()
    {
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
    }
}
