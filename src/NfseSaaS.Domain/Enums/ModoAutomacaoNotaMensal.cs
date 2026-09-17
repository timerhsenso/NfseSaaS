namespace NfseSaaS.Domain.Enums;

/// <summary>
/// O que a automação de Nota Mensal faz quando dispara (ver
/// ConfiguracaoAutomacaoNotaMensal). Automatico chama o mesmo fluxo de
/// emissão em lote que já existe hoje pro clique manual — nenhuma lógica
/// de emissão nova. ListarParaRevisao só monta a lista de candidatos e
/// avisa por e-mail, sem emitir nada sozinho.
/// </summary>
public enum ModoAutomacaoNotaMensal
{
    Automatico = 0,
    ListarParaRevisao = 1
}
