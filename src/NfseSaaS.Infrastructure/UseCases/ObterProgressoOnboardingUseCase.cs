using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Onboarding;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// "Primeiros passos" do painel: leva a conta nova do cadastro até a primeira
/// nota em produção sem precisar de suporte. Não guarda estado próprio — cada
/// passo é deduzido do que já existe na conta (o filtro de tenant do
/// AppDbContext restringe tudo à conta atual), então nunca fica
/// desatualizado e não precisa de migration.
/// Só conta Empresa ativa: uma Empresa desativada não emite.
/// </summary>
public sealed class ObterProgressoOnboardingUseCase : IObterProgressoOnboardingUseCase
{
    public const string LinkAjudaCertificado = "/ajuda/certificado-digital-a1";

    private readonly AppDbContext _db;
    private readonly CertificateFileStore _certificados;
    private readonly ICurrentTenant _currentTenant;

    public ObterProgressoOnboardingUseCase(AppDbContext db, CertificateFileStore certificados, ICurrentTenant currentTenant)
    {
        _db = db;
        _certificados = certificados;
        _currentTenant = currentTenant;
    }

    public async Task<ProgressoOnboardingResponse> ExecutarAsync(CancellationToken cancellationToken)
    {
        var empresas = await _db.Empresas.AsNoTracking()
            .Where(e => e.Ativo)
            .ToListAsync(cancellationToken);

        var tenantId = _currentTenant.TenantId;
        var perfil = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Perfil).FirstOrDefaultAsync(cancellationToken);
        var contador = perfil == PerfilConta.Contador;
        var emailConfirmado = await _db.Users.AsNoTracking().AnyAsync(u => u.TenantId == tenantId && u.EmailConfirmed, cancellationToken);

        var idsEmpresas = empresas.Select(e => e.Id).ToList();
        var temEmpresa = empresas.Count > 0;
        // Empresa criada no cadastro em etapas: existe, mas pode faltar IM e regime.
        var pendencias = empresas.Select(e => e.PendenciasCadastro()).FirstOrDefault(p => p.Count > 0) ?? Array.Empty<string>();
        var temCertificado = idsEmpresas.Any(_certificados.ExisteCertificado);
        var temServico = await _db.Servicos.AsNoTracking().AnyAsync(s => s.Ativo && idsEmpresas.Contains(s.EmpresaId), cancellationToken);
        var temCliente = await _db.Clientes.AsNoTracking().AnyAsync(c => c.Ativo && idsEmpresas.Contains(c.EmpresaId), cancellationToken);
        var temNotaTeste = await _db.NotasFiscais.AsNoTracking()
            .AnyAsync(n => n.Status == NfseStatus.Autorizada && idsEmpresas.Contains(n.EmpresaId), cancellationToken);
        var emProducao = empresas.Any(e => e.TipoAmbiente == TipoAmbiente.Producao);

        var passoEmpresa = (contador, temEmpresa, pendencias.Count) switch
        {
            (true, false, _) => new PassoOnboarding("empresa", "Cadastre a primeira empresa cliente",
                "O CNPJ do cliente que vai emitir; os dados vêm da Receita. Cada cliente usa o próprio certificado A1.",
                false, "/Empresas", "Cadastrar empresa", null),
            (false, false, _) => new PassoOnboarding("empresa", "Cadastre a sua empresa",
                "CNPJ, endereço e regime tributário. Dá para buscar os dados pelo CNPJ.",
                false, "/Empresas", "Cadastrar empresa", null),
            (_, true, > 0) => new PassoOnboarding("empresa", "Complete os dados da empresa",
                $"Já trouxemos os dados da Receita. Falta: {string.Join(", ", pendencias)}.",
                false, "/Empresas", "Completar dados", null),
            _ => new PassoOnboarding("empresa", contador ? "Cadastre a primeira empresa cliente" : "Cadastre a sua empresa",
                "Empresa cadastrada.", true, "/Empresas", "Ir para Empresas", null)
        };

        var passos = new List<PassoOnboarding>
        {
            new("email", "Confirme o seu e-mail",
                "Clique no link que enviamos no cadastro. Sem isso dá para testar tudo, mas não emitir em produção.",
                emailConfirmado, "#reenviar-confirmacao", "Reenviar e-mail", null),
            passoEmpresa,
            new("certificado", "Envie o certificado digital A1",
                "O arquivo .pfx do CNPJ e a senha dele. É com ele que a nota é assinada.",
                temCertificado, "/Empresas", "Enviar certificado", LinkAjudaCertificado),
            new("servico", "Cadastre um serviço",
                "O que você presta, com o código de tributação nacional. Busque pelo nome do serviço.",
                temServico, "/Servicos", "Cadastrar serviço", null),
            new("cliente", "Cadastre um cliente",
                "Para quem você emite a nota. Com CNPJ, os dados vêm da Receita.",
                temCliente, "/Clientes", "Cadastrar cliente", null),
            new("nota-teste", "Emita uma nota de teste",
                "A empresa começa em homologação: a nota é autorizada pela Receita, mas não tem valor fiscal.",
                temNotaTeste || emProducao, "/Nfse", "Emitir nota", null),
            new("producao", "Passe a empresa para produção",
                "Com o teste certo, troque o ambiente da empresa para produção e emita de verdade.",
                emProducao, "/Empresas", "Ir para Empresas", null)
        };

        return new ProgressoOnboardingResponse(passos);
    }
}
