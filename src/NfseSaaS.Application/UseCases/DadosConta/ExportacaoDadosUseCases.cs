using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.DadosConta;

/// <summary>
/// Regras da exportação dos dados da conta (D1), sem banco nem disco:
/// - só administradores pedem e baixam (o arquivo tem os dados de TODA a conta);
/// - um pedido em andamento por vez e até <see cref="MaximoPorDia"/> por dia;
/// - o arquivo vale <see cref="DiasValidade"/> dias e depois é apagado.
/// </summary>
public static class RegrasExportacaoDados
{
    public const int DiasValidade = 7;
    public const int MaximoPorDia = 3;

    public const string MotivoSoAdministrador = "Só o administrador da conta pode exportar os dados.";
    public const string MotivoEmAndamento = "Já existe uma exportação em andamento. Aguarde terminar.";
    public const string MotivoLimiteDiario = "Limite de 3 exportações por dia atingido. Tente de novo amanhã.";
    public const string MotivoIndisponivel = "Este arquivo não está mais disponível. Peça uma nova exportação.";

    /// <summary>Por que a conta não pode pedir agora (nulo = pode).</summary>
    public static string? MotivoParaNaoPedir(bool administrador, bool emAndamento, int pedidosHoje)
    {
        if (!administrador)
            return MotivoSoAdministrador;
        if (emAndamento)
            return MotivoEmAndamento;
        if (pedidosHoje >= MaximoPorDia)
            return MotivoLimiteDiario;
        return null;
    }

    /// <summary>O arquivo pode ser baixado agora?</summary>
    public static bool PodeBaixar(StatusExportacaoDados status, DateTimeOffset? expiraEm, DateTimeOffset agora) =>
        status == StatusExportacaoDados.Pronta && expiraEm is { } expira && expira > agora;

    public static DateTimeOffset Expiracao(DateTimeOffset concluidaEm) => concluidaEm.AddDays(DiasValidade);

    /// <summary>Nome do arquivo baixado: micoempresa-dados-{documento}-{AAAAMMDD-HHmm}.zip (hora de Brasília).</summary>
    public static string NomeArquivo(string documentoConta, DateTimeOffset geradaEm)
    {
        var documento = new string(documentoConta.Where(char.IsLetterOrDigit).ToArray());
        if (documento.Length == 0)
            documento = "conta";
        return $"micoempresa-dados-{documento}-{geradaEm.ToOffset(TimeSpan.FromHours(-3)):yyyyMMdd-HHmm}.zip";
    }
}

/// <summary>Uma exportação vista na tela "Meus dados".</summary>
public sealed record ExportacaoDadosResponse(
    Guid Id,
    StatusExportacaoDados Status,
    DateTimeOffset PedidaEm,
    DateTimeOffset? ConcluidaEm,
    DateTimeOffset? ExpiraEm,
    long? TamanhoBytes,
    int? QuantidadeNotas,
    string? Erro,
    bool PodeBaixar);

/// <param name="PodeExportar">Quem está vendo é administrador da conta.</param>
/// <param name="Motivo">Por que não pode pedir agora (nulo = pode).</param>
public sealed record ExportacoesDadosResponse(
    bool PodeExportar,
    string? Motivo,
    int DiasValidade,
    IReadOnlyList<ExportacaoDadosResponse> Exportacoes);

/// <summary>Arquivo pronto para o download.</summary>
public sealed record ArquivoExportacaoDados(string Caminho, string NomeArquivo);

/// <summary>Tela "Meus dados": pedir, listar e baixar a exportação da conta atual.</summary>
public interface IExportacaoDadosUseCase
{
    Task<ExportacoesDadosResponse> ListarAsync(CancellationToken cancellationToken);

    /// <summary>Cria o pedido e agenda a geração. RegraNegocioException quando não pode.</summary>
    Task<ExportacaoDadosResponse> SolicitarAsync(CancellationToken cancellationToken);

    /// <summary>Arquivo para baixar. RegraNegocioException quando expirou ou não é administrador.</summary>
    Task<ArquivoExportacaoDados> BaixarAsync(Guid exportacaoId, CancellationToken cancellationToken);
}

/// <summary>Resultado da limpeza diária (para o log).</summary>
public sealed record ResultadoLimpezaExportacoes(int Expiradas, int ArquivosApagados);

/// <summary>Lado do job: montar o arquivo e apagar o que expirou.</summary>
public interface IGerarExportacaoDadosUseCase
{
    Task GerarAsync(Guid exportacaoId, CancellationToken cancellationToken);

    Task<ResultadoLimpezaExportacoes> LimparAsync(DateTimeOffset agora, CancellationToken cancellationToken);
}
