using System.Globalization;

namespace NfseSaaS.Application.UseCases.Cobranca;

/// <summary>
/// NFS-e da mensalidade (N1), sem banco nem SEFIN. Cada pagamento
/// confirmado no Asaas vira uma nota da conta emissora (a LFMS), com o
/// Serviço escolhido na configuração — o item da LC 116, o NBS e o
/// IBS/CBS (cIndOp/cClassTrib) ficam no cadastro desse Serviço, então
/// trocar o item é editar o Serviço (ou apontar para outro), sem código.
/// </summary>
public static class RegrasNotaMensalidade
{
    /// <summary>Tentativas automáticas (uma por passada do job) antes de desistir.</summary>
    public const int MaximoTentativas = 8;

    /// <summary>Só pagamentos registrados nestes últimos dias entram (ligar a configuração não emite o histórico inteiro).</summary>
    public const int JanelaDias = 35;

    /// <summary>Chave de idempotência da emissão: repetir nunca duplica a nota.</summary>
    public static string ChaveIdempotencia(string pagamentoId) => $"mensalidade:{pagamentoId}";

    public static string Descricao(string? nomePlano, DateOnly dataPagamento, string pagamentoId)
    {
        var plano = string.IsNullOrWhiteSpace(nomePlano) ? "" : $" — plano {nomePlano.Trim()}";
        return $"Licença de uso do sistema micoempresa{plano}. Mensalidade paga em {dataPagamento.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} (fatura {pagamentoId}).";
    }

    /// <summary>Apelido do tomador criado na conta emissora (único por empresa: desempata com o fim do documento).</summary>
    public static string Apelido(string nome, string documento, bool desempatar, int maximo)
    {
        var baseNome = nome.Trim();
        if (!desempatar)
            return baseNome.Length <= maximo ? baseNome : baseNome[..maximo].TrimEnd();
        var sufixo = " " + (documento.Length >= 4 ? documento[^4..] : documento);
        var corte = Math.Max(1, maximo - sufixo.Length);
        return (baseNome.Length <= corte ? baseNome : baseNome[..corte].TrimEnd()) + sufixo;
    }
}

/// <param name="Desligado">Sem NotaMensalidade:EmpresaId/ServicoId: nada foi olhado.</param>
public sealed record ResultadoNotasMensalidade(bool Desligado, int Registradas, int Emitidas, int ComErro, int Ignoradas);

/// <summary>Job: registra os pagamentos novos e emite as notas pendentes (e as que falharam).</summary>
public interface INotasMensalidadeUseCase
{
    Task<ResultadoNotasMensalidade> ExecutarAsync(DateTimeOffset agora, CancellationToken cancellationToken);
}
