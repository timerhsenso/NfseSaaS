using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Nacional.Validation;

/// <summary>Validações locais da DPS antes do envio (campos obrigatórios, formatos), independentes da validação feita pela própria SEFIN.</summary>
public interface IDpsValidator
{
    IReadOnlyCollection<string> Validar(DpsRequest request);
}
