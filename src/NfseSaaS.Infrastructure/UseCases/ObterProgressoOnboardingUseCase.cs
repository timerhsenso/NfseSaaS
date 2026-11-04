using Microsoft.EntityFrameworkCore;
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

    public ObterProgressoOnboardingUseCase(AppDbContext db, CertificateFileStore certificados)
    {
        _db = db;
        _certificados = certificados;
    }

    public async Task<ProgressoOnboardingResponse> ExecutarAsync(CancellationToken cancellationToken)
    {
        var empresas = await _db.Empresas.AsNoTracking()
            .Where(e => e.Ativo)
            .Select(e => new { e.Id, e.TipoAmbiente })
            .ToListAsync(cancellationToken);

        var idsEmpresas = empresas.Select(e => e.Id).ToList();
        var temEmpresa = empresas.Count > 0;
        var temCertificado = idsEmpresas.Any(_certificados.ExisteCertificado);
        var temServico = await _db.Servicos.AsNoTracking().AnyAsync(s => s.Ativo && idsEmpresas.Contains(s.EmpresaId), cancellationToken);
        var temCliente = await _db.Clientes.AsNoTracking().AnyAsync(c => c.Ativo && idsEmpresas.Contains(c.EmpresaId), cancellationToken);
        var temNotaTeste = await _db.NotasFiscais.AsNoTracking()
            .AnyAsync(n => n.Status == NfseStatus.Autorizada && idsEmpresas.Contains(n.EmpresaId), cancellationToken);
        var emProducao = empresas.Any(e => e.TipoAmbiente == TipoAmbiente.Producao);

        var passos = new List<PassoOnboarding>
        {
            new("empresa", "Cadastre a sua empresa",
                "CNPJ, endereço e regime tributário. Dá para buscar os dados pelo CNPJ.",
                temEmpresa, "/Empresas", "Cadastrar empresa", null),
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
