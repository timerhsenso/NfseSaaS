namespace NfseSaaS.Application.Authorization;

/// <summary>Códigos estáveis dos tipos de e-mail — ver EmailLog.Tipo.</summary>
public static class TipoEmailCatalogo
{
    public const string Convite = "Convite";
    public const string ResetSenha = "ResetSenha";
    public const string EsqueciSenha = "EsqueciSenha";
    public const string AvisoTrocaSenha = "AvisoTrocaSenha";

    /// <summary>Verificação em duas etapas ligada ou desligada pelo próprio usuário.</summary>
    public const string AvisoDuploFator = "AvisoDuploFator";

    /// <summary>Link para confirmar o e-mail da conta (cadastro ou "Reenviar" no aviso do topo).</summary>
    public const string ConfirmacaoEmail = "ConfirmacaoEmail";

    /// <summary>Nota autorizada (PDF e XML anexos) enviada ao tomador.</summary>
    public const string NfseTomador = "NfseTomador";

    /// <summary>Certificado digital da Empresa vencendo (30/15/7 dias) ou vencido.</summary>
    public const string CertificadoVencendo = "CertificadoVencendo";

    /// <summary>DOC1: documento da Empresa (certidão etc.) vencendo em até 15 dias ou vencido.</summary>
    public const string DocumentoVencendo = "DocumentoVencendo";

    /// <summary>Reajustes de aniversário gerados pelo job: aguardando aprovação e/ou aplicados automaticamente.</summary>
    public const string ReajustesAniversario = "ReajustesAniversario";

    /// <summary>Alerta técnico para o operador da plataforma: erro no sistema ou job que falhou.</summary>
    public const string AlertaOperador = "AlertaOperador";

    /// <summary>Avisos de cobrança aos administradores: teste acabando, fatura criada/vencendo/vencida, bloqueio, cancelamento, pagamento.</summary>
    public const string AvisoCobranca = "AvisoCobranca";

    /// <summary>Aviso prévio (30 dias) de reajuste do preço de tabela para quem já assina.</summary>
    public const string ReajusteAssinatura = "ReajusteAssinatura";

    /// <summary>Convite enviado pela tela "Indique e ganhe" (Reply-To = quem indicou).</summary>
    public const string ConviteIndicacao = "ConviteIndicacao";

    /// <summary>Aviso prévio (15 dias) de nova versão dos Termos de uso e da Política de privacidade.</summary>
    public const string NovosTermos = "NovosTermos";

    /// <summary>Exportação dos dados da conta pronta para baixar (tela "Meus dados").</summary>
    public const string ExportacaoDados = "ExportacaoDados";

    /// <summary>Aviso aos administradores de que a conta foi encerrada (e quando os dados serão apagados).</summary>
    public const string EncerramentoConta = "EncerramentoConta";

    /// <summary>Aviso de que a conta (assinatura cancelada) será encerrada automaticamente numa data.</summary>
    public const string AvisoEncerramentoConta = "AvisoEncerramentoConta";

    /// <summary>RC1: link para quem recebe a nota como cópia confirmar que quer sair das cópias.</summary>
    public const string SaidaCopiaConfirmacao = "SaidaCopiaConfirmacao";

    /// <summary>RC1: aviso ao fornecedor de que alguém saiu das cópias de um cliente.</summary>
    public const string SaidaCopiaAviso = "SaidaCopiaAviso";
}
