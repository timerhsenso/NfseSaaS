using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Empresa emissora de NFS-e dentro de um Tenant (o SaaS é multiempresa:
/// um mesmo Tenant pode ter mais de uma Empresa/filial emitindo notas,
/// cada uma com seu próprio certificado digital e inscrição municipal).
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class Empresa : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public string Cnpj { get; set; } = string.Empty;

    public string RazaoSocial { get; set; } = string.Empty;

    public string? NomeFantasia { get; set; }

    public string InscricaoMunicipal { get; set; } = string.Empty;

    /// <summary>Código do município (IBGE) onde a empresa está estabelecida.</summary>
    public string CodigoMunicipio { get; set; } = string.Empty;

    public string Telefone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    // --- Endereço (cadastral — não é exigido no bloco <prest> da DPS, mas
    // é necessário para correspondência, DANFSe/relatórios e para manter o
    // cadastro da Empresa completo) ---

    public string Cep { get; set; } = string.Empty;

    public string Logradouro { get; set; } = string.Empty;

    public string Numero { get; set; } = string.Empty;

    public string? Complemento { get; set; }

    public string Bairro { get; set; } = string.Empty;

    /// <summary>UF (sigla, 2 letras) do endereço — CodigoMunicipio já identifica o município, mas a UF é útil para exibição sem precisar resolver a tabela IBGE.</summary>
    public string Uf { get; set; } = string.Empty;

    // --- Regime tributário (necessário para montar a DPS — ver <regTrib> e <trib> no layout da NFS-e Nacional) ---

    /// <summary>Opção pelo Simples Nacional ("1" Não optante, "2" Optante MEI, "3" Optante Simples Nacional exceto MEI).</summary>
    public string OpSimpNac { get; set; } = string.Empty;

    public string RegApTribSN { get; set; } = string.Empty;

    public string RegEspTrib { get; set; } = string.Empty;

    public string TribIssqn { get; set; } = string.Empty;

    /// <summary>
    /// Alíquota de ISS (%) que vai na DPS quando o tomador retém o ISS.
    /// Pra ME/EPP do Simples é a alíquota efetiva da faixa de faturamento
    /// (RBT12, Anexo III/IV/V da LC 123) — muda com o faturamento, por isso
    /// é editável aqui e pode ser sobrescrita na própria nota. A retenção
    /// em si é decidida pelo Cliente (Cliente.RetemIss), não pela Empresa.
    /// </summary>
    public decimal? AliquotaIss { get; set; }

    public string CstPisCofins { get; set; } = string.Empty;

    /// <summary>
    /// C6: no Simples, acrescenta à descrição da nota sem retenção federal a
    /// frase de dispensa (IN RFB 765/2007 e IN SRF 459/2004). Muitos
    /// tomadores pedem, para não reter por engano.
    /// </summary>
    public bool AvisoDispensaRetencaoNaDescricao { get; set; }

    /// <summary>Percentual total de tributos do Simples Nacional (campo pTotTribSN da DPS), ex.: "3.00".</summary>
    public string PercentualTotalTributosSimplesNacional { get; set; } = string.Empty;

    public bool Ativo { get; set; } = true;

    /// <summary>
    /// Em que ambiente do SEFIN Nacional as notas desta Empresa são
    /// emitidas (vira o tpAmb da DPS — ver DpsBuilder). Toda Empresa
    /// nasce em Homologacao; a troca de ambiente é deliberada e
    /// auditada nos dois sentidos (ver IAlterarAmbienteEmpresaUseCase),
    /// nunca o valor default.
    /// </summary>
    public TipoAmbiente TipoAmbiente { get; set; } = TipoAmbiente.Homologacao;

    /// <summary>
    /// Último NSU (Número Sequencial Único) já processado na sincronização
    /// com o ADN (Distribuição de DF-e) — ver SincronizarNotasDaSefinUseCase.
    /// Null/0 = nunca sincronizado, próxima chamada começa do zero.
    /// </summary>
    public long? UltimoNsuDistribuicao { get; set; }

    /// <summary>
    /// Com quantos dias de antecedência um Contrato entra em "Vencendo em
    /// breve" antes da data prevista de reajuste, pra quem não define um
    /// valor próprio no Contrato (Contrato.DiasAlertaOverride). Padrão de
    /// mercado razoável: 30.
    /// </summary>
    public int DiasAlertaReajusteContratoPadrao { get; set; } = 30;

    /// <summary>
    /// Padrão dos Contratos desta Empresa no aniversário: true aplica o
    /// reajuste calculado pelo índice na hora (sem aprovação); false deixa
    /// "aguardando aprovação". Cada Contrato pode sobrescrever
    /// (Contrato.ReajusteAutomatico).
    /// </summary>
    public bool AplicarReajusteAutomatico { get; set; }

    /// <summary>Modelo do e-mail da nota desta empresa. Nulo = o padrão da conta.</summary>
    public Guid? ModeloEmailNotaId { get; set; }

    /// <summary>
    /// O que falta no cadastro para emitir nota (em palavras para o usuário).
    /// A Empresa criada no cadastro em etapas nasce só com os dados públicos
    /// do CNPJ; Inscrição Municipal e regime ficam para o usuário completar.
    /// Mesmos campos obrigatórios do CadastrarEmpresaRequestValidator (a
    /// validação de FORMATO continua lá; aqui é só "está preenchido?").
    /// </summary>
    public IReadOnlyList<string> PendenciasCadastro()
    {
        var pendencias = new List<string>();
        if (string.IsNullOrWhiteSpace(InscricaoMunicipal))
            pendencias.Add("Inscrição Municipal");
        if (string.IsNullOrWhiteSpace(CodigoMunicipio) || string.IsNullOrWhiteSpace(Uf)
            || string.IsNullOrWhiteSpace(Cep) || string.IsNullOrWhiteSpace(Logradouro)
            || string.IsNullOrWhiteSpace(Numero) || string.IsNullOrWhiteSpace(Bairro))
            pendencias.Add("endereço completo");
        if (string.IsNullOrWhiteSpace(Telefone))
            pendencias.Add("telefone");
        if (string.IsNullOrWhiteSpace(Email))
            pendencias.Add("e-mail");
        if (string.IsNullOrWhiteSpace(OpSimpNac) || string.IsNullOrWhiteSpace(RegApTribSN) || string.IsNullOrWhiteSpace(RegEspTrib)
            || string.IsNullOrWhiteSpace(TribIssqn) || string.IsNullOrWhiteSpace(CstPisCofins)
            || (OpSimpNac is "2" or "3" && string.IsNullOrWhiteSpace(PercentualTotalTributosSimplesNacional)))
            pendencias.Add("dados fiscais (regime e tributação)");
        return pendencias;
    }
}
