using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.PainelFinanceiro;

/// <summary>De quais empresas o painel soma os números.</summary>
public enum EscopoPainelFinanceiro
{
    /// <summary>Todas as empresas da conta (padrão do perfil Empresa: matriz + filiais).</summary>
    Todas = 1,
    /// <summary>Só a empresa escolhida no topo (padrão do perfil Contador: cada empresa é um cliente do escritório).</summary>
    Empresa = 2
}

/// <param name="EmpresaAtualId">Empresa escolhida no topo da tela (cookie). Inválida ou de outra conta = ignorada.</param>
/// <param name="Escopo">Nulo = padrão pelo perfil da conta.</param>
/// <param name="IncluirContratos">Quem não consulta Contratos não vê receita recorrente, notas a emitir nem reajustes.</param>
/// <param name="Hoje">Só para testes; nulo = hoje em Brasília.</param>
public sealed record PainelFinanceiroRequest(
    Guid? EmpresaAtualId,
    EscopoPainelFinanceiro? Escopo,
    bool IncluirContratos,
    DateOnly? Hoje = null);

public sealed record PainelFinanceiroResponse(
    EscopoPainelFinanceiro Escopo,
    Guid? EmpresaId,
    string? EmpresaNome,
    int QuantidadeEmpresas,
    DateOnly Hoje,
    FaturamentoMesResponse MesAtual,
    FaturamentoMesResponse MesAnterior,
    decimal AcumuladoAno,
    int NotasAno,
    decimal Ultimos12Meses,
    IReadOnlyList<FaturamentoMesResponse> Meses,
    LimiteRegimeResponse? Limite,
    IReadOnlyList<ClienteFaturamentoResponse> MaioresClientes,
    decimal TotalMaioresClientesJanela,
    int EmpresasEmHomologacao,
    ContratosPainelResponse? Contratos,
    bool TemDados);

/// <summary>Faturamento de um mês (competência), só notas de produção autorizadas.</summary>
/// <param name="Contratos">Notas ligadas a um contrato (recorrente).</param>
/// <param name="Avulsas">Notas sem contrato.</param>
public sealed record FaturamentoMesResponse(DateOnly Competencia, decimal Contratos, decimal Avulsas, int Notas)
{
    public decimal Total => Contratos + Avulsas;
}

/// <summary>
/// Onde a empresa está em relação ao limite do regime (só quando o painel
/// mostra UMA empresa — somar empresas diferentes não diz nada sobre o limite de nenhuma).
/// </summary>
/// <param name="Tipo">"mei" ou "simples".</param>
/// <param name="Nivel">"ok", "atencao" (perto do limite) ou "alerta" (passou).</param>
public sealed record LimiteRegimeResponse(
    string Tipo,
    string Titulo,
    string Texto,
    decimal Base,
    decimal? Limite,
    decimal? Percentual,
    string Nivel);

public sealed record ClienteFaturamentoResponse(Guid ClienteId, string Nome, decimal Valor, decimal Percentual);

public sealed record ContratosPainelResponse(
    decimal ReceitaRecorrente,
    int ContratosMensais,
    decimal AEmitirValor,
    int AEmitirQuantidade,
    int AEmitirRejeitadas,
    IReadOnlyList<ContratoAEmitirResponse> AEmitir,
    DateOnly ReajustesAte,
    int ReajustesQuantidade,
    decimal ReajustesImpactoEstimado,
    int ReajustesSemEstimativa,
    IReadOnlyList<ReajustePrevistoResponse> Reajustes);

/// <param name="Rejeitada">Teve nota rejeitada no mês e nenhuma autorizada: precisa de atenção.</param>
public sealed record ContratoAEmitirResponse(
    Guid ContratoId,
    Guid EmpresaId,
    string Cliente,
    string Contrato,
    decimal Valor,
    bool Rejeitada);

/// <param name="Situacao">"aguardando" (aguardando aprovação, valor já calculado), "estimado" (pelo índice), "sem-indice" (sem estimativa).</param>
/// <param name="Atrasado">Aniversário já passou e o reajuste não foi registrado.</param>
public sealed record ReajustePrevistoResponse(
    Guid ContratoId,
    Guid EmpresaId,
    string Cliente,
    string Contrato,
    DateOnly Data,
    bool Atrasado,
    string Situacao,
    string? Indice,
    decimal ValorAtual,
    decimal? ValorNovo,
    decimal? Percentual,
    string? Observacao);

public interface IObterPainelFinanceiroUseCase
{
    Task<PainelFinanceiroResponse> ExecutarAsync(PainelFinanceiroRequest request, CancellationToken cancellationToken);
}
