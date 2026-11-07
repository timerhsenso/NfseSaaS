using Microsoft.AspNetCore.Identity;

namespace NfseSaaS.Infrastructure.Identity;

/// <summary>
/// Mensagens do ASP.NET Identity em português (senha fraca, e-mail já usado,
/// link expirado...). Antes saíam em inglês nas telas de cadastro, convite,
/// troca e redefinição de senha. Os códigos (Code) continuam os do Identity;
/// só o texto muda. O usuário entra com o e-mail, então "nome de usuário"
/// vira "e-mail" nas mensagens.
/// </summary>
public sealed class IdentityErrorDescriberPtBr : IdentityErrorDescriber
{
    private static IdentityError Erro(string codigo, string mensagem) => new() { Code = codigo, Description = mensagem };

    public override IdentityError DefaultError() =>
        Erro(nameof(DefaultError), "Não foi possível concluir a operação. Tente de novo.");

    public override IdentityError ConcurrencyFailure() =>
        Erro(nameof(ConcurrencyFailure), "Os dados foram alterados por outra pessoa ao mesmo tempo. Atualize a página e tente de novo.");

    public override IdentityError PasswordMismatch() =>
        Erro(nameof(PasswordMismatch), "Senha incorreta.");

    public override IdentityError InvalidToken() =>
        Erro(nameof(InvalidToken), "Este link expirou ou já foi usado. Peça um novo (\"Esqueci minha senha\" no login ou um novo convite ao administrador).");

    public override IdentityError RecoveryCodeRedemptionFailed() =>
        Erro(nameof(RecoveryCodeRedemptionFailed), "Código de recuperação inválido.");

    public override IdentityError LoginAlreadyAssociated() =>
        Erro(nameof(LoginAlreadyAssociated), "Já existe um usuário com este acesso.");

    public override IdentityError InvalidUserName(string? userName) =>
        Erro(nameof(InvalidUserName), $"O e-mail \"{userName}\" é inválido.");

    public override IdentityError InvalidEmail(string? email) =>
        Erro(nameof(InvalidEmail), $"O e-mail \"{email}\" é inválido.");

    public override IdentityError DuplicateUserName(string userName) =>
        Erro(nameof(DuplicateUserName), $"O e-mail \"{userName}\" já está em uso.");

    public override IdentityError DuplicateEmail(string email) =>
        Erro(nameof(DuplicateEmail), $"O e-mail \"{email}\" já está em uso.");

    public override IdentityError InvalidRoleName(string? role) =>
        Erro(nameof(InvalidRoleName), $"O perfil \"{role}\" é inválido.");

    public override IdentityError DuplicateRoleName(string role) =>
        Erro(nameof(DuplicateRoleName), $"O perfil \"{role}\" já existe.");

    public override IdentityError UserAlreadyHasPassword() =>
        Erro(nameof(UserAlreadyHasPassword), "Este usuário já tem senha definida.");

    public override IdentityError UserLockoutNotEnabled() =>
        Erro(nameof(UserLockoutNotEnabled), "O bloqueio não está habilitado para este usuário.");

    public override IdentityError UserAlreadyInRole(string role) =>
        Erro(nameof(UserAlreadyInRole), $"O usuário já tem o perfil \"{role}\".");

    public override IdentityError UserNotInRole(string role) =>
        Erro(nameof(UserNotInRole), $"O usuário não tem o perfil \"{role}\".");

    public override IdentityError PasswordTooShort(int length) =>
        Erro(nameof(PasswordTooShort), $"A senha precisa ter pelo menos {length} caracteres.");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Erro(nameof(PasswordRequiresUniqueChars), $"A senha precisa ter pelo menos {uniqueChars} caracteres diferentes.");

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Erro(nameof(PasswordRequiresNonAlphanumeric), "A senha precisa ter pelo menos um símbolo (por exemplo: ! @ # $ %).");

    public override IdentityError PasswordRequiresDigit() =>
        Erro(nameof(PasswordRequiresDigit), "A senha precisa ter pelo menos um número (0-9).");

    public override IdentityError PasswordRequiresLower() =>
        Erro(nameof(PasswordRequiresLower), "A senha precisa ter pelo menos uma letra minúscula (a-z).");

    public override IdentityError PasswordRequiresUpper() =>
        Erro(nameof(PasswordRequiresUpper), "A senha precisa ter pelo menos uma letra maiúscula (A-Z).");
}
