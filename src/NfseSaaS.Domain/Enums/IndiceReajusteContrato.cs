namespace NfseSaaS.Domain.Enums;

/// <summary>
/// Índices de reajuste contratual mais comuns no mercado brasileiro.
/// Virou enum (era texto livre) a pedido do usuário — troca consciente:
/// ganha consistência de cadastro, perde flexibilidade (um índice novo
/// exige adicionar aqui e recompilar). "Outro" cobre o caso residual sem
/// abrir mão do enum.
/// </summary>
public enum IndiceReajusteContrato
{
    Ipca = 0,
    Igpm = 1,
    Incc = 2,
    Inpc = 3,
    Selic = 4,
    Cdi = 5,
    Outro = 99
}
