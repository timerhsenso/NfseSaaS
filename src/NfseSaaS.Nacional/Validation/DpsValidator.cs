using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Nacional.Validation;

/// <summary>
/// PLACEHOLDER — Fase 1.
/// Validações de negócio (ex.: faixa de série por tipo de emissor, CNPJ/CPF
/// válido, código de tributação nacional autorizado para o município) serão
/// definidas na Fase 2, com base no que já foi observado/comprovado na POC
/// (erros E0008, E0010 da SEFIN). Não inventar regra fiscal nesta fase.
/// </summary>
public sealed class DpsValidator : IDpsValidator
{
    public IReadOnlyCollection<string> Validar(DpsRequest request) => Array.Empty<string>();
}
