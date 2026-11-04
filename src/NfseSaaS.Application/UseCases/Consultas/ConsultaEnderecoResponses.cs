namespace NfseSaaS.Application.UseCases.Consultas;

/// <summary>Endereço de um CEP, com o código IBGE do município (o que a DPS exige).</summary>
public sealed record ConsultaCepResponse(
    string Cep,
    string? Logradouro,
    string? Complemento,
    string? Bairro,
    string Municipio,
    string Uf,
    string CodigoMunicipio);

/// <summary>Nome e UF de um município pelo código IBGE de 7 dígitos.</summary>
public sealed record ConsultaMunicipioResponse(string CodigoMunicipio, string Nome, string? Uf);

/// <summary>Preenche o endereço a partir do CEP (fonte pública: ViaCEP).</summary>
public interface IConsultarCepUseCase
{
    Task<ConsultaCepResponse> ExecutarAsync(string cep, CancellationToken cancellationToken);
}

/// <summary>Nome do município a partir do código IBGE (fonte pública: API de localidades do IBGE).</summary>
public interface IConsultarMunicipioUseCase
{
    Task<ConsultaMunicipioResponse> ExecutarAsync(string codigoMunicipio, CancellationToken cancellationToken);
}
