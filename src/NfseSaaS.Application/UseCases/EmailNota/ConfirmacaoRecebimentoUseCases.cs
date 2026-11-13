using NfseSaaS.Application.Exceptions;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Application.UseCases.EmailNota;

/// <summary>Confirmação já feita, como a página pública mostra (e-mail mascarado).</summary>
public sealed record ConfirmacaoRecebimentoPublica(string EmailMascarado, DateTimeOffset ConfirmadoEm);

/// <summary>Página do link [Confirmar recebimento]: a nota e quem já confirmou.</summary>
public sealed record ContextoConfirmacaoRecebimento(
    string Empresa,
    string Numero,
    string Valor,
    string Competencia,
    IReadOnlyList<ConfirmacaoRecebimentoPublica> Confirmacoes);

/// <summary>Resultado do clique em "Confirmar".</summary>
/// <param name="JaConfirmado">Este e-mail já tinha confirmado (nada foi gravado de novo).</param>
public sealed record ResultadoConfirmacaoRecebimento(string Empresa, string EmailMascarado, bool JaConfirmado);

/// <summary>Confirmação para a conta logada (detalhe da nota): e-mail inteiro, IP, data.</summary>
public sealed record ConfirmacaoRecebimentoResponse(string Email, bool Principal, string? Ip, DateTimeOffset ConfirmadoEm);

/// <summary>Link assinado do campo [Confirmar recebimento] (com validade).</summary>
public interface ILinkConfirmacaoRecebimento
{
    /// <summary>URL completa para o e-mail da nota, válida por <paramref name="dias"/>.</summary>
    string Gerar(Guid tenantId, Guid nfseId, int dias);

    /// <summary>Página de exemplo (prévia e "Mandar um teste para mim"): não confirma nada.</summary>
    string Exemplo { get; }

    /// <summary>Token válido e no prazo → conta e nota; senão nulo.</summary>
    (Guid TenantId, Guid NfseId)? Ler(string? token);
}

/// <summary>
/// CR1: o destinatário do e-mail da nota confirma que recebeu. Link do campo
/// [Confirmar recebimento] → página pública com a nota → digita o próprio
/// e-mail (precisa estar entre os destinatários: principal ou cópia) →
/// "Confirmar" (POST, para leitor de link não confirmar sozinho) → grava
/// e-mail, IP e hora; a lista de notas mostra o envelope verde.
/// </summary>
public interface IConfirmacaoRecebimentoUseCase
{
    Task<ContextoConfirmacaoRecebimento?> AbrirAsync(string? token, CancellationToken cancellationToken);

    Task<ResultadoConfirmacaoRecebimento> ConfirmarAsync(string? token, string? email, string? ip, CancellationToken cancellationToken);

    /// <summary>Quem confirmou esta nota (conta logada).</summary>
    Task<IReadOnlyList<ConfirmacaoRecebimentoResponse>> ListarDaNotaAsync(Guid nfseId, CancellationToken cancellationToken);
}

/// <summary>CR1: regras puras da confirmação de recebimento.</summary>
public static class RegrasConfirmacaoRecebimento
{
    public const string MensagemLinkInvalido =
        "Este link expirou ou não é válido. Para confirmar, responda o e-mail da nota e fale com a empresa.";

    public const string MensagemForaDaLista =
        "Este e-mail não está entre os destinatários desta nota. Use o endereço em que você recebeu o e-mail.";

    /// <summary>Validade do link conferida (1 a 365 dias).</summary>
    public static int ValidarDias(int dias) =>
        dias is < ModeloEmailNota.ValidadeConfirmacaoMinimaDias or > ModeloEmailNota.ValidadeConfirmacaoMaximaDias
            ? throw new RegraNegocioException(
                $"O link de confirmação pode valer de {ModeloEmailNota.ValidadeConfirmacaoMinimaDias} a {ModeloEmailNota.ValidadeConfirmacaoMaximaDias} dias.")
            : dias;

    /// <summary>"carlos@gmail.com" → "ca***@gm***.com"; "ana@empresa.com.br" → "an***@em***.com.br".</summary>
    public static string Mascarar(string email)
    {
        var arroba = email.IndexOf('@');
        if (arroba <= 0 || arroba == email.Length - 1)
            return "***";
        var nome = email[..arroba];
        var dominio = email[(arroba + 1)..];
        var ponto = dominio.IndexOf('.');
        var rotulo = ponto > 0 ? dominio[..ponto] : dominio;
        var final = ponto > 0 ? dominio[ponto..] : string.Empty;
        return $"{Inicio(nome)}***@{Inicio(rotulo)}***{final}";
    }

    private static string Inicio(string texto) => texto.Length <= 2 ? texto[..1] : texto[..2];

    /// <summary>
    /// Destinatários de todos os envios da nota (principal e cópias). A cópia
    /// oculta fixa do modelo (arquivo do próprio fornecedor) não entra.
    /// </summary>
    /// <returns>E-mail (minúsculo) → era principal em algum envio.</returns>
    public static IReadOnlyDictionary<string, bool> Destinatarios(IEnumerable<(string Destinatario, string? Copias)> envios)
    {
        var resultado = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (destinatario, copias) in envios)
        {
            if (!string.IsNullOrWhiteSpace(destinatario))
                resultado[destinatario.Trim().ToLowerInvariant()] = true;
            foreach (var copia in NfseSaaS.Application.UseCases.Clientes.EmailsCopiaCliente.Separar(copias))
            {
                var chave = copia.Trim().ToLowerInvariant();
                if (chave.Length > 0 && !resultado.ContainsKey(chave))
                    resultado[chave] = false;
            }
        }
        return resultado;
    }
}
