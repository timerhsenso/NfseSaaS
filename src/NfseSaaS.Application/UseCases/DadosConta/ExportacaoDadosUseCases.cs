using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.DadosConta;

/// <summary>
/// Regras da exportação dos dados da conta (D1), sem banco nem disco:
/// - só administradores pedem e baixam (o arquivo tem os dados de TODA a conta);
/// - UMA exportação a cada <see cref="DiasValidade"/> dias: enquanto a última
///   estiver pronta e valendo, a conta baixa essa (não monta outra). Se
///   falhou ou sumiu, pode pedir de novo na hora; o operador pode liberar
///   antes (expira a atual);
/// - um pedido em andamento por vez e, como trava contra falha em série,
///   até <see cref="MaximoPorDia"/> pedidos por dia;
/// - teto de disco para todas as contas juntas (ExportacaoDados:LimiteTotalMb).
/// O uso do dia a dia (XML para o contador) é o "XML do mês" da tela de
/// notas, leve e sem limite.
/// </summary>
public static class RegrasExportacaoDados
{
    public const int DiasValidade = 7;
    public const int MaximoPorDia = 3;

    public const string MotivoSoAdministrador = "Só o administrador da conta pode exportar os dados.";
    public const string MotivoEmAndamento = "Já existe uma exportação em andamento. Aguarde terminar.";
    public const string MotivoLimiteDiario = "Limite de 3 pedidos por dia atingido. Tente de novo amanhã.";
    public const string MotivoIndisponivel = "Este arquivo não está mais disponível. Peça uma nova exportação.";
    public const string MotivoServidorCheio = "O servidor está com muitas exportações guardadas agora. Tente de novo amanhã.";

    /// <summary>Por que a conta não pode pedir agora (nulo = pode).</summary>
    /// <param name="prontaAte">Validade da última exportação pronta da conta (nulo = não há uma valendo).</param>
    public static string? MotivoParaNaoPedir(bool administrador, bool emAndamento, DateTimeOffset? prontaAte, int pedidosHoje, bool discoCheio = false)
    {
        if (!administrador)
            return MotivoSoAdministrador;
        if (emAndamento)
            return MotivoEmAndamento;
        if (prontaAte is { } ate)
            return MotivoJaTemPronta(ate);
        if (pedidosHoje >= MaximoPorDia)
            return MotivoLimiteDiario;
        if (discoCheio)
            return MotivoServidorCheio;
        return null;
    }

    public static string MotivoJaTemPronta(DateTimeOffset ate) =>
        $"Você já tem uma exportação pronta, disponível até {ate.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy}: baixe essa. "
        + $"Uma nova pode ser pedida quando ela expirar (uma a cada {DiasValidade} dias). Para o XML das notas, use \"XML do mês\" na tela de notas.";

    public const string MotivoNadaALiberar = "Esta conta não tem exportação pronta: ela já pode pedir uma nova.";

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

    /// <summary>Operador: expira a exportação pronta da conta (e apaga o arquivo) para ela pedir outra antes dos 7 dias.</summary>
    Task LiberarNovaPelaPlataformaAsync(Guid tenantId, Guid operadorId, CancellationToken cancellationToken);
}

/// <summary>Resultado da limpeza diária (para o log).</summary>
public sealed record ResultadoLimpezaExportacoes(int Expiradas, int ArquivosApagados);

/// <summary>Lado do job: montar o arquivo e apagar o que expirou.</summary>
public interface IGerarExportacaoDadosUseCase
{
    Task GerarAsync(Guid exportacaoId, CancellationToken cancellationToken);

    Task<ResultadoLimpezaExportacoes> LimparAsync(DateTimeOffset agora, CancellationToken cancellationToken);
}
