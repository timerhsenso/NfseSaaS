namespace NfseSaaS.Infrastructure.Identity;

/// <summary>
/// Configuração do operador da plataforma — seção "Plataforma" do
/// appsettings (ou variáveis de ambiente
/// Plataforma__UsuariosOperadoresIds__0, __1, ...).
///
/// Identifica o operador pelo Id do usuário (AspNetUsers.Id), NÃO pelo
/// e-mail: o cadastro é aberto e não exige confirmação de e-mail, então
/// qualquer pessoa poderia criar uma conta com o e-mail do operador e
/// herdar o acesso. O Id é gerado pelo sistema e não dá pra forjar.
/// Lista vazia = ninguém é operador (Logs, Sobre e /hangfire ficam
/// fechados pra todo mundo) — falha segura.
/// </summary>
public sealed class PlataformaOptions
{
    public const string SectionName = "Plataforma";

    public List<Guid> UsuariosOperadoresIds { get; set; } = new();

    /// <summary>
    /// false = a tela "Criar conta" recusa novos cadastros (beta fechado:
    /// novos clientes entram só por convite do operador). Padrão true pra
    /// não mudar o comportamento atual sem decisão explícita.
    /// </summary>
    public bool CadastroAberto { get; set; } = true;
}
