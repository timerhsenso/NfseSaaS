using System.Security.Cryptography;
using System.Text;
using FluentValidation;

namespace NfseSaaS.Application.UseCases.Plataforma;

/// <summary>
/// Código de confirmação da exclusão de conta (Plataforma › Contas), sem
/// banco nem e-mail, testável isolado. O código nunca é guardado: só o hash
/// SHA-256 dele, em memória, por <see cref="MinutosValidade"/> minutos e no
/// máximo <see cref="MaxTentativas"/> tentativas. Usado uma vez só.
/// </summary>
public static class CodigoExclusaoConta
{
    public const int Digitos = 6;
    public const int MinutosValidade = 10;
    public const int MaxTentativas = 5;

    /// <summary>Seis dígitos aleatórios (gerador criptográfico), com zeros à esquerda.</summary>
    public static string Gerar() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static byte[] Hash(string codigo) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(codigo.Trim()));

    /// <summary>Compara em tempo constante (não vaza, pelo tempo de resposta, quantos dígitos acertou).</summary>
    public static bool Confere(byte[] hashEsperado, string? codigoDigitado) =>
        !string.IsNullOrWhiteSpace(codigoDigitado)
        && CryptographicOperations.FixedTimeEquals(hashEsperado, Hash(codigoDigitado));

    /// <summary>"carlos@micoempresa.com.br" → "c****s@micoempresa.com.br" (para a tela dizer para onde foi).</summary>
    public static string MascararEmail(string email)
    {
        var arroba = email.IndexOf('@');
        if (arroba <= 0)
            return "***";
        var usuario = email[..arroba];
        var visivel = usuario.Length <= 2
            ? usuario[..1] + "*"
            : usuario[0] + new string('*', Math.Min(usuario.Length - 2, 6)) + usuario[^1];
        return visivel + email[arroba..];
    }
}

/// <summary>O que será apagado e para onde foi o código (resposta de "Excluir conta").</summary>
public sealed record SolicitacaoExclusaoContaResponse(
    string EmailMascarado,
    int MinutosValidade,
    int Empresas,
    int Clientes,
    int Contratos,
    int Servicos,
    int Usuarios);

public sealed record ConfirmarExclusaoContaRequest(string Codigo);

public sealed class ConfirmarExclusaoContaRequestValidator : AbstractValidator<ConfirmarExclusaoContaRequest>
{
    public ConfirmarExclusaoContaRequestValidator()
    {
        RuleFor(x => x.Codigo)
            .NotEmpty().WithMessage("Informe o código recebido por e-mail.")
            .Matches("^[0-9]{6}$").WithMessage("O código tem 6 números.");
    }
}

/// <summary>
/// Passo 1: confere se a conta pode ser excluída (nunca registrou nota, sem
/// assinatura ativa, não é a conta da plataforma) e envia o código ao e-mail
/// do OPERADOR logado.
/// </summary>
public interface ISolicitarExclusaoContaUseCase
{
    Task<SolicitacaoExclusaoContaResponse> ExecutarAsync(Guid tenantId, Guid operadorId, CancellationToken cancellationToken);
}

/// <summary>
/// Passo 2: com o código certo e dentro do prazo, apaga a conta inteira
/// (Empresas, Clientes, Serviços, Contratos e documentos, usuários, grupos,
/// históricos, e-mails, certificados e jobs). As mesmas regras do passo 1
/// são conferidas de novo.
/// </summary>
public interface IConfirmarExclusaoContaUseCase
{
    Task ExecutarAsync(Guid tenantId, Guid operadorId, string codigo, CancellationToken cancellationToken);
}
