using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using NfseSaaS.Application.Exceptions;

namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>Página do link do rodapé: de qual fornecedor é o pedido.</summary>
public sealed record ContextoSaidaCopia(string Empresa);

public enum ResultadoPedidoSaidaCopia
{
    /// <summary>Resposta neutra: se o e-mail é uma cópia, a confirmação foi para ele (não revela se está na lista).</summary>
    ConfirmacaoEnviada = 1,
    /// <summary>É o destinatário principal: não sai por aqui.</summary>
    Principal = 2
}

/// <summary>Página do link de confirmação.</summary>
/// <param name="EmailMascarado">"fu***@empresa.com.br".</param>
public sealed record ConfirmacaoSaidaCopia(string Empresa, string EmailMascarado, bool Confirmada, bool Expirada);

/// <summary>Quem pediu para sair das cópias de um cliente (cadastro do cliente).</summary>
public sealed record SaidaCopiaClienteResponse(string Email, DateTimeOffset ConfirmadoEm, string? NumeroNota);

/// <summary>Link "pedir para sair das cópias" do rodapé (assinado, com validade).</summary>
public interface ILinkSaidaCopia
{
    /// <summary>URL completa para o rodapé do e-mail da nota.</summary>
    string Gerar(Guid tenantId, Guid nfseId);

    /// <summary>Token válido e no prazo → conta e nota; senão nulo.</summary>
    (Guid TenantId, Guid NfseId)? Ler(string? token);
}

/// <summary>
/// RC1: quem recebe a nota como cópia pede para sair. Fluxo: link do rodapé →
/// digita o e-mail → confirmação vai para esse endereço → confirma (POST, para
/// leitor de link não confirmar sozinho) → sai das cópias daquele cliente,
/// com histórico e aviso ao fornecedor. O principal nunca sai.
/// </summary>
public interface ISaidaCopiaEmailUseCase
{
    Task<ContextoSaidaCopia?> AbrirAsync(string? token, CancellationToken cancellationToken);

    Task<ResultadoPedidoSaidaCopia> PedirAsync(string? token, string? email, string? ip, CancellationToken cancellationToken);

    Task<ConfirmacaoSaidaCopia?> ObterConfirmacaoAsync(string? codigo, CancellationToken cancellationToken);

    Task<ConfirmacaoSaidaCopia> ConfirmarAsync(string? codigo, string? ip, CancellationToken cancellationToken);

    /// <summary>Histórico de quem saiu (cadastro do cliente, conta logada).</summary>
    Task<IReadOnlyList<SaidaCopiaClienteResponse>> ListarDoClienteAsync(Guid clienteId, CancellationToken cancellationToken);
}

/// <summary>RC1: regras puras da saída das cópias.</summary>
public static class RegrasSaidaCopia
{
    /// <summary>O link do rodapé vale por este tempo depois do envio da nota.</summary>
    public static readonly TimeSpan ValidadeLink = TimeSpan.FromDays(90);

    /// <summary>O link de confirmação vale por este tempo.</summary>
    public static readonly TimeSpan ValidadeConfirmacao = TimeSpan.FromHours(24);

    /// <summary>Pedidos de confirmação por e-mail e cliente em 24 horas (contra uso da página para mandar e-mail a terceiros).</summary>
    public const int MaximoPedidosPorDia = 3;

    /// <summary>Minúsculo e sem espaços; nulo se não for um e-mail.</summary>
    public static string? Normalizar(string? email)
    {
        var texto = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (texto.Length is < 5 or > EmailsCopiaCliente.TamanhoMaximoEndereco || texto.Contains(' '))
            return null;
        try
        {
            return new MailAddress(texto).Address == texto ? texto : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string NormalizarOuErro(string? email) =>
        Normalizar(email) ?? throw new RegraNegocioException("Informe um e-mail válido.");

    public static bool EhCopia(string? colunaCopias, string email) =>
        EmailsCopiaCliente.Separar(colunaCopias).Any(c => string.Equals(c.Trim(), email, StringComparison.OrdinalIgnoreCase));

    public static bool EhPrincipal(string? emailPrincipal, string email) =>
        string.Equals((emailPrincipal ?? string.Empty).Trim(), email, StringComparison.OrdinalIgnoreCase);

    /// <summary>A coluna de cópias sem este e-mail (nulo quando não sobra nenhum).</summary>
    public static string? Remover(string? colunaCopias, string email) =>
        EmailsCopiaCliente.Juntar(EmailsCopiaCliente.Separar(colunaCopias)
            .Where(c => !string.Equals(c.Trim(), email, StringComparison.OrdinalIgnoreCase)));

    /// <summary>"fulano@empresa.com.br" → "fu***@empresa.com.br".</summary>
    public static string Mascarar(string email)
    {
        var arroba = email.IndexOf('@');
        if (arroba <= 0)
            return "***";
        var nome = email[..arroba];
        var visivel = nome.Length <= 2 ? nome[..1] : nome[..2];
        return $"{visivel}***{email[arroba..]}";
    }

    /// <summary>Código do link de confirmação (vai no e-mail) e o hash que fica no banco.</summary>
    public static (string Codigo, string Hash) NovoCodigo()
    {
        var codigo = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (codigo, Hash(codigo));
    }

    public static string Hash(string codigo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(codigo))).ToLowerInvariant();
}
