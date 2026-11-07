using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Cobranca;

/// <summary>
/// Campanha de fundador (Roadmap, seção 2), sem banco nem gateway:
/// - a vaga é guardada para quem ENTRA (e-mail confirmado) enquanto a
///   campanha do grupo está aberta (sobram vagas e não passou a data-fim);
/// - a reserva vale até <see cref="Planos.DiasReservaFundadorAposTeste"/>
///   dias depois do fim do teste grátis; feita no beta (cobrança desligada
///   para a conta), vale sem prazo;
/// - assinar consome a reserva; cancelar perde o preço de fundador.
/// As vagas usadas de um grupo são as assinaturas fundador (inclusive
/// canceladas: a vaga foi usada) mais as reservas válidas.
/// </summary>
public static class RegrasFundador
{
    /// <summary>Grupo de vagas pelo perfil da conta: contador concorre às vagas dos escritórios.</summary>
    public static PublicoPlano GrupoDoPerfil(PerfilConta perfil) =>
        perfil == PerfilConta.Contador ? PublicoPlano.Escritorios : PublicoPlano.Empresas;

    /// <summary>Campanha aberta pela data: sem data-fim, ou hoje até a data-fim (inclusive).</summary>
    public static bool CampanhaAbertaNaData(DateOnly? campanhaAte, DateOnly hoje) =>
        campanhaAte is not { } fim || hoje <= fim;

    /// <summary>
    /// Teste iniciado a partir deste instante ainda segura a reserva
    /// (fim do teste + dias de reserva ≥ agora). Usado na contagem do banco.
    /// </summary>
    public static DateTimeOffset InicioTesteMinimoParaReserva(DateTimeOffset agora) =>
        agora.AddDays(-(Planos.TesteDias + Planos.DiasReservaFundadorAposTeste));

    /// <summary>A reserva ainda garante o preço de fundador agora?</summary>
    public static bool ReservaValida(
        DateTimeOffset? reservadoEm,
        bool semPrazo,
        bool fundador,
        StatusAssinatura status,
        DateTimeOffset? testeIniciadoEm,
        DateTimeOffset agora)
    {
        if (reservadoEm is null || fundador || status == StatusAssinatura.Cancelada)
            return false;
        if (semPrazo || testeIniciadoEm is null)
            return true;
        return testeIniciadoEm.Value >= InicioTesteMinimoParaReserva(agora);
    }

    /// <summary>
    /// Último dia (Brasília) em que a reserva vale. Nulo quando não tem
    /// prazo ainda: reserva do beta, ou teste que não começou (o prazo só
    /// passa a correr quando o teste começa).
    /// </summary>
    public static DateOnly? ReservaValeAte(bool semPrazo, DateTimeOffset? testeIniciadoEm) =>
        semPrazo || testeIniciadoEm is not { } inicio
            ? null
            : DiaEmBrasilia(inicio.AddDays(Planos.TesteDias + Planos.DiasReservaFundadorAposTeste));

    /// <summary>
    /// Pode guardar uma vaga agora? Conta que nunca assinou, sem reserva e
    /// sem preço de fundador, com e-mail confirmado e vaga sobrando no grupo
    /// (<paramref name="vagasRestantes"/> já é zero com a campanha encerrada).
    /// </summary>
    public static bool PodeReservar(
        bool emailConfirmado,
        bool contaAtiva,
        bool isenta,
        DateTimeOffset? reservadoEm,
        bool fundador,
        DateTimeOffset? assinadaEm,
        int vagasRestantes) =>
        emailConfirmado && contaAtiva && !isenta
        && reservadoEm is null && !fundador && assinadaEm is null
        && vagasRestantes > 0;

    /// <summary>
    /// Ao assinar, a conta ganha o preço de fundador do plano? Pela própria
    /// reserva válida no grupo do plano, ou por vaga sobrando no grupo.
    /// </summary>
    public static bool AssinaComoFundador(bool reservaValida, PublicoPlano? grupoDaReserva, PublicoPlano grupoDoPlano, int vagasRestantesNoGrupo) =>
        (reservaValida && grupoDaReserva == grupoDoPlano) || vagasRestantesNoGrupo > 0;

    private static DateOnly DiaEmBrasilia(DateTimeOffset quando) =>
        DateOnly.FromDateTime(quando.ToUniversalTime().AddHours(-3).DateTime);
}
