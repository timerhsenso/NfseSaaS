using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Contratos;

/// <summary>
/// Cliente/Empresa não fazem parte deste DTO (não editáveis — trocar o
/// vínculo de um Contrato depois de criado não faz sentido de negócio;
/// é outro Contrato). ValorAtual/DataUltimoReajuste também ficam de fora
/// DE PROPÓSITO: editar o valor aqui pularia o histórico de reajustes
/// (ContratoReajuste, Parte 3) — a única forma prevista de mudar o valor
/// é pelo fluxo dedicado de "Registrar reajuste". Pelo mesmo motivo, as
/// linhas de Servicos (Fase 6) também não entram aqui — recompor quais
/// serviços um Contrato cobra é uma operação diferente de reajuste
/// (muda escopo, não preço) e fica pra um endpoint dedicado quando
/// houver demanda real, mesmo padrão do resto do projeto.
/// </summary>
public sealed record AtualizarContratoRequest(
    string Descricao,
    DateOnly DataInicioContrato,
    int PeriodicidadeReajusteMeses,
    IndiceReajusteContrato? IndiceReajuste,
    int? DiasAlertaOverride,
    string? Observacao,
    StatusContrato Status,
    DateOnly? DataFim,
    TipoCobrancaContrato TipoCobranca,
    bool PermitirAlterarValorNaEmissao,
    // Linhas de serviço do Contrato. Nulo = não mexe nas linhas (compatível
    // com quem chama sem elas). Informado = passa a ser a composição
    // COMPLETA do Contrato: linhas ausentes são removidas, novas incluídas,
    // e o ValorAtual é recalculado — registrado no Histórico.
    IReadOnlyList<ContratoServicoRequest>? Servicos = null,
    // Obrigatórios quando as linhas MUDAM (vira um evento do Histórico de
    // valores): é reajuste ou alteração de escopo, a partir de quando vale,
    // e por quê.
    TipoMovimentacaoValor? TipoAlteracaoServicos = null,
    DateOnly? VigenciaAlteracaoServicos = null,
    string? MotivoAlteracaoServicos = null,
    bool? ReajusteAutomatico = null,
    bool AplicarIndiceNegativo = false);
