using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Identity;

/// <summary>Uma célula (Incluir, Alterar, Excluir, Consultar) da matriz — ver DefinicaoGrupoPadrao.</summary>
public readonly record struct PermissaoIaec(bool Incluir, bool Alterar, bool Excluir, bool Consultar)
{
    public static readonly PermissaoIaec Nenhuma = new(false, false, false, false);
    public static readonly PermissaoIaec SoConsultar = new(false, false, false, true);
    public static readonly PermissaoIaec Tudo = new(true, true, true, true);
}

public sealed class GrupoProvisionamentoService : IGrupoProvisionamentoService
{
    private readonly AppDbContext _db;

    public GrupoProvisionamentoService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Matriz IAEC dos 5 grupos padrão — cada linha aqui é a tradução
    /// direta de um [Authorize(Roles = ...)] que já existia no código
    /// antes do módulo de segurança (Empresas/Clientes/Servicos/
    /// Contratos: escrita só Administrador, leitura geral; Nfse: emitir
    /// = Administrador+Emissor, cancelar = só Administrador; Usuarios/
    /// Grupos: só Administrador). A única permissão NOVA em relação ao
    /// que já existia é Auditoria pra Financeiro/Contador — decisão
    /// tomada explicitamente com o usuário, os outros 4 papéis não
    /// enxergavam Auditoria e continuam sem enxergar.
    /// </summary>
    private static readonly (string Nome, string? Descricao, bool EhAdministrador, Dictionary<string, PermissaoIaec> Permissoes)[] DefinicaoGruposPadrao =
    {
        ("Administrador", "Acesso completo a todas as telas.", true, new Dictionary<string, PermissaoIaec>
        {
            [TelaCatalogo.Empresas] = PermissaoIaec.Tudo,
            [TelaCatalogo.Clientes] = PermissaoIaec.Tudo,
            [TelaCatalogo.Servicos] = PermissaoIaec.Tudo,
            [TelaCatalogo.Contratos] = PermissaoIaec.Tudo,
            [TelaCatalogo.Nfse] = new PermissaoIaec(Incluir: true, Alterar: false, Excluir: true, Consultar: true), // Excluir aqui = Cancelar; não existe "Alterar" pra Nfse (imutável após emitida)
            [TelaCatalogo.Usuarios] = PermissaoIaec.Tudo,
            [TelaCatalogo.Grupos] = PermissaoIaec.Tudo,
            [TelaCatalogo.Auditoria] = PermissaoIaec.SoConsultar // Auditoria é só leitura por natureza, não tem Incluir/Alterar/Excluir manual
        }),
        ("Emissor", "Pode emitir Nfse. Leitura nas demais telas de cadastro.", false, new Dictionary<string, PermissaoIaec>
        {
            [TelaCatalogo.Empresas] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Clientes] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Servicos] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Contratos] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Nfse] = new PermissaoIaec(Incluir: true, Alterar: false, Excluir: false, Consultar: true),
            [TelaCatalogo.Usuarios] = PermissaoIaec.Nenhuma,
            [TelaCatalogo.Grupos] = PermissaoIaec.Nenhuma,
            [TelaCatalogo.Auditoria] = PermissaoIaec.Nenhuma
        }),
        ("Financeiro", "Leitura de Nfse e Auditoria para conciliação financeira.", false, new Dictionary<string, PermissaoIaec>
        {
            [TelaCatalogo.Empresas] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Clientes] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Servicos] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Contratos] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Nfse] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Usuarios] = PermissaoIaec.Nenhuma,
            [TelaCatalogo.Grupos] = PermissaoIaec.Nenhuma,
            [TelaCatalogo.Auditoria] = PermissaoIaec.SoConsultar
        }),
        ("Contador", "Leitura de Nfse e Auditoria para fins fiscais/contábeis.", false, new Dictionary<string, PermissaoIaec>
        {
            [TelaCatalogo.Empresas] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Clientes] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Servicos] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Contratos] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Nfse] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Usuarios] = PermissaoIaec.Nenhuma,
            [TelaCatalogo.Grupos] = PermissaoIaec.Nenhuma,
            [TelaCatalogo.Auditoria] = PermissaoIaec.SoConsultar
        }),
        ("Consulta", "Só leitura — sem acesso a Usuários, Grupos ou Auditoria.", false, new Dictionary<string, PermissaoIaec>
        {
            [TelaCatalogo.Empresas] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Clientes] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Servicos] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Contratos] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Nfse] = PermissaoIaec.SoConsultar,
            [TelaCatalogo.Usuarios] = PermissaoIaec.Nenhuma,
            [TelaCatalogo.Grupos] = PermissaoIaec.Nenhuma,
            [TelaCatalogo.Auditoria] = PermissaoIaec.Nenhuma
        })
    };

    public async Task<IReadOnlyDictionary<string, Guid>> ProvisionarGruposPadraoAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // Ver TenantIdOverrideDeSistema em AppDbContext: chamado tanto em
        // Registrar (ninguém autenticado ainda) quanto no backfill de
        // startup (fora de requisição HTTP) — em nenhum dos dois casos
        // ICurrentTenant.TenantId resolve pelo Claim normalmente, então
        // ApplyTenantIsolation rejeitaria a gravação sem este override.
        _db.TenantIdOverrideDeSistema = tenantId;

        try
        {
            var telasPorCodigo = await _db.Telas.AsNoTracking().ToDictionaryAsync(t => t.Codigo, t => t.Id, cancellationToken);

            var resultado = new Dictionary<string, Guid>();

            foreach (var (nome, descricao, ehAdministrador, permissoes) in DefinicaoGruposPadrao)
            {
                var grupo = new Grupo
                {
                    TenantId = tenantId,
                    Nome = nome,
                    Descricao = descricao,
                    Padrao = true,
                    EhAdministrador = ehAdministrador
                };
                _db.Grupos.Add(grupo);

                foreach (var (telaCodigo, permissao) in permissoes)
                {
                    if (!telasPorCodigo.TryGetValue(telaCodigo, out var telaId))
                        continue; // Tela ainda não seedada (não deveria acontecer — TelaSeeder roda antes) — ignora em vez de quebrar o provisionamento inteiro.

                    _db.GrupoTelas.Add(new GrupoTela
                    {
                        TenantId = tenantId,
                        GrupoId = grupo.Id,
                        TelaId = telaId,
                        Incluir = permissao.Incluir,
                        Alterar = permissao.Alterar,
                        Excluir = permissao.Excluir,
                        Consultar = permissao.Consultar
                    });
                }

                resultado[nome] = grupo.Id;
            }

            await _db.SaveChangesAsync(cancellationToken);

            return resultado;
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }
    }
}
