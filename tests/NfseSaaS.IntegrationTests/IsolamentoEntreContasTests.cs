using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// L9 — isolamento entre contas pela API de verdade (login por cadastro, com
/// CSRF): a conta B, sabendo os ids da conta A, não lê, não altera, não
/// apaga e não usa nada dela (empresa, cliente, serviço, contrato, nota,
/// PDF, grupo, auditoria). Cada leitura tem o controle positivo: a própria
/// conta A enxerga o mesmo id, então o 404 de B é o filtro, não um id errado.
/// SEG1 estendeu para as rotas novas (documentos, modelos e e-mails, Nota
/// Mensal, painel financeiro, exportações, importação, recebimento,
/// certificado e segurança da conta).
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class IsolamentoEntreContasTests
{
    private readonly AplicacaoTesteFactory _factory;

    public IsolamentoEntreContasTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed record Conta(HttpClient Cliente, string Cnpj, Guid TenantId, Guid EmpresaId, Guid GrupoId, Guid UsuarioId);

    private sealed record DadosA(Guid ClienteId, Guid ServicoId, Guid ContratoId, Guid NfseId);

    [Fact]
    public async Task Conta_B_nao_le_nao_altera_e_nao_usa_nada_da_conta_A()
    {
        var cnpjs = new List<string>();
        try
        {
            var a = await CriarContaAsync(cnpjs);
            var b = await CriarContaAsync(cnpjs);
            var dados = await CriarDadosAsync(a);

            // ---------- Leituras por id: A vê (200), B não (404) ----------
            string[] leituras =
            [
                $"/api/empresas/{a.EmpresaId}",
                $"/api/empresas/{a.EmpresaId}/certificado",
                $"/api/clientes/{dados.ClienteId}",
                $"/api/servicos/{dados.ServicoId}",
                $"/api/contratos/{dados.ContratoId}",
                $"/api/contratos/{dados.ContratoId}/historico-valores",
                $"/api/nfse/{dados.NfseId}",
                $"/api/nfse/{dados.NfseId}/eventos",
                $"/api/nfse/{dados.NfseId}/xml",
                $"/api/grupos/{a.GrupoId}"
            ];
            foreach (var url in leituras)
            {
                var dono = await a.Cliente.GetAsync(url);
                Assert.True(dono.StatusCode == HttpStatusCode.OK, $"A deveria ver {url}, veio {(int)dono.StatusCode}.");

                var outro = await b.Cliente.GetAsync(url);
                Assert.True(outro.StatusCode == HttpStatusCode.NotFound, $"B não pode ver {url}: veio {(int)outro.StatusCode}.");
            }

            // XML da nota: A baixa com o mesmo nome do PDF (.xml).
            var xmlDeA = await a.Cliente.GetAsync($"/api/nfse/{dados.NfseId}/xml");
            Assert.Equal("application/xml", xmlDeA.Content.Headers.ContentType?.MediaType);
            Assert.EndsWith(".xml", xmlDeA.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.StartsWith("ClienteA_", xmlDeA.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.Contains("infNFSe", await xmlDeA.Content.ReadAsStringAsync());

            // PDF da nota de A (não pode nem chegar a gerar).
            var pdf = await b.Cliente.GetAsync($"/api/nfse/{dados.NfseId}/danfse-pdf");
            Assert.Equal(HttpStatusCode.NotFound, pdf.StatusCode);

            // ---------- Listas de B não trazem nada de A ----------
            foreach (var url in new[]
                     {
                         "/api/empresas",
                         $"/api/clientes?empresaId={a.EmpresaId}",
                         $"/api/servicos?empresaId={a.EmpresaId}",
                         $"/api/contratos?empresaId={a.EmpresaId}",
                         $"/api/nfse?empresaId={a.EmpresaId}",
                         "/api/grupos",
                         "/api/auditlogs"
                     })
            {
                var resposta = await b.Cliente.GetAsync(url);
                var corpo = await resposta.Content.ReadAsStringAsync();
                Assert.True(resposta.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound, $"{url}: veio {(int)resposta.StatusCode}.");
                // No 404 ("Empresa {id} não encontrada") o id da empresa é só o que B mandou de volta.
                var idsDeA = resposta.StatusCode == HttpStatusCode.NotFound
                    ? new[] { dados.ClienteId, dados.ServicoId, dados.ContratoId, dados.NfseId, a.GrupoId }
                    : new[] { a.EmpresaId, dados.ClienteId, dados.ServicoId, dados.ContratoId, dados.NfseId, a.GrupoId };
                foreach (var id in idsDeA)
                    Assert.False(corpo.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase), $"{url} ({(int)resposta.StatusCode}) trouxe {id}: {corpo}");
                Assert.DoesNotContain(a.Cnpj, corpo);
            }

            // ---------- Escritas de B sobre os ids de A: nenhuma passa ----------
            var cliente = new
            {
                nome = "Invasor", apelido = "Invasor", email = (string?)null, telefone = (string?)null,
                codigoMunicipio = "2927408", cep = "40000000", logradouro = "Rua X", numero = "1",
                complemento = (string?)null, bairro = "Centro", uf = "BA"
            };
            await AssertNaoPassaAsync(b.Cliente.PutAsJsonAsync($"/api/clientes/{dados.ClienteId}", cliente), "alterar cliente de A");
            await AssertNaoPassaAsync(b.Cliente.PostAsync($"/api/clientes/{dados.ClienteId}/desativar", null), "desativar cliente de A");
            await AssertNaoPassaAsync(b.Cliente.DeleteAsync($"/api/clientes/{dados.ClienteId}"), "excluir cliente de A");
            await AssertNaoPassaAsync(b.Cliente.PostAsync($"/api/servicos/{dados.ServicoId}/desativar", null), "desativar serviço de A");
            await AssertNaoPassaAsync(b.Cliente.DeleteAsync($"/api/servicos/{dados.ServicoId}"), "excluir serviço de A");
            await AssertNaoPassaAsync(b.Cliente.DeleteAsync($"/api/contratos/{dados.ContratoId}"), "excluir contrato de A");
            await AssertNaoPassaAsync(b.Cliente.PostAsync($"/api/empresas/{a.EmpresaId}/desativar", null), "desativar empresa de A");
            await AssertNaoPassaAsync(b.Cliente.DeleteAsync($"/api/empresas/{a.EmpresaId}"), "excluir empresa de A");
            await AssertNaoPassaAsync(b.Cliente.PutAsJsonAsync($"/api/empresas/{a.EmpresaId}/ambiente", new { tipoAmbiente = 1 }), "mudar ambiente da empresa de A");
            await AssertNaoPassaAsync(b.Cliente.PostAsync($"/api/nfse/{dados.NfseId}/cancelar", JsonContent.Create(new { codigoMotivo = 1, motivo = "Cancelamento indevido de teste" })), "cancelar nota de A");
            await AssertNaoPassaAsync(b.Cliente.PostAsync($"/api/nfse/{dados.NfseId}/enviar-email", null), "reenviar nota de A");
            await AssertNaoPassaAsync(b.Cliente.DeleteAsync($"/api/grupos/{a.GrupoId}"), "excluir grupo de A");

            // Usuários: B não mexe no usuário de A nem põe o seu num grupo de A.
            await AssertNaoPassaAsync(b.Cliente.PostAsync($"/api/auth/usuarios/{a.UsuarioId}/bloquear", null), "bloquear usuário de A");
            await AssertNaoPassaAsync(b.Cliente.PostAsync($"/api/auth/usuarios/{a.UsuarioId}/resetar-senha", null), "resetar senha do usuário de A");
            await AssertNaoPassaAsync(b.Cliente.PutAsJsonAsync($"/api/auth/usuarios/{a.UsuarioId}/grupo", new { grupoId = b.GrupoId }), "mudar grupo do usuário de A");
            await AssertNaoPassaAsync(b.Cliente.DeleteAsync($"/api/auth/usuarios/{a.UsuarioId}"), "excluir usuário de A");
            // Aqui o usuário é de B (existe): a recusa é "Grupo inválido" (400), da checagem de conta do grupo.
            var grupoDeA = await b.Cliente.PutAsJsonAsync($"/api/auth/usuarios/{b.UsuarioId}/grupo", new { grupoId = a.GrupoId });
            Assert.Equal(HttpStatusCode.BadRequest, grupoDeA.StatusCode);
            Assert.Contains("Grupo inválido", await grupoDeA.Content.ReadAsStringAsync());
            var usuariosDeB = await (await b.Cliente.GetAsync("/api/auth/usuarios")).Content.ReadAsStringAsync();
            Assert.DoesNotContain(a.UsuarioId.ToString(), usuariosDeB, StringComparison.OrdinalIgnoreCase);

            // Criar coisas "dentro" da empresa de A, ou usando cliente/serviço de A na empresa de B.
            await AssertNaoPassaAsync(b.Cliente.PostAsJsonAsync("/api/clientes", new
            {
                empresaId = a.EmpresaId, cpfCnpj = "52998224725", nome = "Invasor", apelido = "Invasor2",
                codigoMunicipio = "2927408", cep = "40000000", logradouro = "Rua X", numero = "1", bairro = "Centro", uf = "BA"
            }), "criar cliente na empresa de A");
            await AssertNaoPassaAsync(b.Cliente.PostAsJsonAsync("/api/servicos", new
            {
                empresaId = a.EmpresaId, descricao = "Invasor", codigoTributacaoNacional = "010101", codigoNbs = "115022000", valorPadrao = 10m
            }), "criar serviço na empresa de A");
            await AssertNaoPassaAsync(b.Cliente.PostAsJsonAsync("/api/contratos", new
            {
                empresaId = b.EmpresaId, clienteId = dados.ClienteId, descricao = "Invasor",
                servicos = new[] { new { servicoId = dados.ServicoId, quantidade = 1m, valorUnitario = 10m } },
                dataInicioContrato = "2026-11-01", periodicidadeReajusteMeses = 12, indiceReajuste = 0, status = 1, tipoCobranca = 1,
                permitirAlterarValorNaEmissao = false
            }), "contrato de B com cliente e serviço de A");

            // Lote de PDFs com a nota de A: nada de A sai no zip.
            var lote = await b.Cliente.PostAsJsonAsync("/api/nfse/danfse-pdf/lote", new { nfseIds = new[] { dados.NfseId } });
            if (lote.IsSuccessStatusCode)
                Assert.Contains(dados.NfseId.ToString(), string.Join(',', lote.Headers.TryGetValues("X-Nfse-Ids-Ignorados", out var v) ? v : []), StringComparison.OrdinalIgnoreCase);

            // ---------- SEG1: rotas das entregas de 09 a 14/11 ----------
            var extras = await CriarDadosExtrasAsync(a, dados);
            string[] leiturasNovas =
            [
                $"/api/documentos-empresa?empresaId={a.EmpresaId}",
                $"/api/documentos-empresa/historico?empresaId={a.EmpresaId}&tipo=1",
                $"/api/documentos-empresa/escolha?empresaId={a.EmpresaId}",
                $"/api/clientes/documentos-exigidos?clienteId={dados.ClienteId}&empresaId={a.EmpresaId}",
                $"/api/nota-mensal/candidatos?empresaId={a.EmpresaId}&competencia=2026-11-01",
                $"/api/nfse/{dados.NfseId}/recebimento",
                $"/api/importacao/modelo?empresaId={a.EmpresaId}"
            ];
            foreach (var url in leiturasNovas)
            {
                var dono = await a.Cliente.GetAsync(url);
                Assert.True(dono.StatusCode == HttpStatusCode.OK, $"A deveria ver {url}, veio {(int)dono.StatusCode}.");

                var outro = await b.Cliente.GetAsync(url);
                Assert.True(outro.StatusCode == HttpStatusCode.NotFound, $"B não pode ver {url}: veio {(int)outro.StatusCode}.");
            }

            // Listas de B (modelos de e-mail, e-mails enviados, exportações, painel) não trazem nada de A.
            foreach (var url in new[]
                     {
                         "/api/modelos-email-nota",
                         "/api/modelos-email-nota/opcoes",
                         "/api/emails",
                         "/api/dados-conta/exportacoes",
                         "/api/documentos-empresa/resumo",
                         $"/api/painel-financeiro?empresaAtualId={a.EmpresaId}&escopo=empresa",
                         $"/api/painel-financeiro?empresaAtualId={a.EmpresaId}&escopo=todas",
                         "/api/conta/seguranca"
                     })
            {
                var resposta = await b.Cliente.GetAsync(url);
                var corpo = await resposta.Content.ReadAsStringAsync();
                Assert.True(resposta.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound, $"{url}: veio {(int)resposta.StatusCode}.");
                foreach (var marca in new[] { extras.ModeloId.ToString(), extras.EmailId.ToString(), extras.DocumentoId.ToString(), a.Cnpj, "Modelo da conta A", "emaildacontaa@teste.com", $"iso{a.Cnpj}@teste.com" })
                    Assert.False(corpo.Contains(marca, StringComparison.OrdinalIgnoreCase), $"{url} trouxe {marca}: {corpo}");
            }

            await AssertNaoPassaAsync(b.Cliente.PutAsJsonAsync($"/api/modelos-email-nota/{extras.ModeloId}", new { nome = "Invasor", assunto = "x", mensagem = "x" }), "alterar modelo de e-mail de A");
            await AssertNaoPassaAsync(b.Cliente.PostAsync($"/api/modelos-email-nota/{extras.ModeloId}/padrao", null), "tornar padrão o modelo de A");
            await AssertNaoPassaAsync(b.Cliente.DeleteAsync($"/api/modelos-email-nota/{extras.ModeloId}"), "excluir modelo de e-mail de A");
            await AssertNaoPassaAsync(b.Cliente.PostAsync($"/api/emails/{extras.EmailId}/reenviar", null), "reenviar e-mail de A");
            await AssertNaoPassaAsync(b.Cliente.GetAsync($"/api/documentos-empresa/{extras.DocumentoId}/download"), "baixar documento de A");
            await AssertNaoPassaAsync(b.Cliente.DeleteAsync($"/api/documentos-empresa/{extras.DocumentoId}"), "excluir documento de A");
            using (var certificado = new MultipartFormDataContent())
            {
                certificado.Add(new ByteArrayContent([1, 2, 3]), "arquivo", "a.pfx");
                certificado.Add(new StringContent("senha"), "senha");
                await AssertNaoPassaAsync(b.Cliente.PostAsync($"/api/empresas/{a.EmpresaId}/certificado", certificado), "trocar o certificado da empresa de A");
            }

            // ---------- Telas do operador: cliente comum não entra ----------
            foreach (var (metodo, url) in new[]
                     {
                         (HttpMethod.Get, "/api/plataforma/tenants"),
                         (HttpMethod.Post, $"/api/plataforma/tenants/{a.TenantId}/cobranca/conciliar"),
                         (HttpMethod.Get, "/api/logs"),
                         (HttpMethod.Get, "/hangfire")
                     })
            {
                var resposta = await b.Cliente.SendAsync(new HttpRequestMessage(metodo, url));
                Assert.True(resposta.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound,
                    $"Cliente comum em {url}: veio {(int)resposta.StatusCode}.");
            }

            // ---------- No banco, nada de A mudou e nada foi criado nela por B ----------
            using var escopo = _factory.Services.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
            var clienteA = await db.Clientes.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == dados.ClienteId);
            Assert.Equal("Cliente da conta A", clienteA.Nome);
            Assert.True(clienteA.Ativo);
            Assert.True((await db.Servicos.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == dados.ServicoId)).Ativo);
            Assert.True(await db.Contratos.IgnoreQueryFilters().AnyAsync(c => c.Id == dados.ContratoId));
            Assert.Equal(NfseStatus.Autorizada, (await db.NotasFiscais.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.Id == dados.NfseId)).Status);
            var empresaA = await db.Empresas.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Id == a.EmpresaId);
            Assert.True(empresaA.Ativo);
            Assert.Equal(TipoAmbiente.Homologacao, empresaA.TipoAmbiente);
            Assert.True(await db.Grupos.IgnoreQueryFilters().AnyAsync(g => g.Id == a.GrupoId));
            var usuarioA = await db.Users.AsNoTracking().SingleAsync(u => u.Id == a.UsuarioId);
            Assert.Equal(a.GrupoId, usuarioA.GrupoId);
            Assert.True(usuarioA.LockoutEnd is null || usuarioA.LockoutEnd < DateTimeOffset.UtcNow);
            Assert.Equal(b.GrupoId, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == b.UsuarioId)).GrupoId);
            Assert.Equal(1, await db.Clientes.IgnoreQueryFilters().CountAsync(c => c.EmpresaId == a.EmpresaId));
            Assert.Equal(1, await db.Servicos.IgnoreQueryFilters().CountAsync(s => s.EmpresaId == a.EmpresaId));
            Assert.False(await db.Contratos.IgnoreQueryFilters().AnyAsync(c => c.TenantId == b.TenantId));
            Assert.False(await db.EmailLogs.IgnoreQueryFilters().AnyAsync(e => e.TenantId == b.TenantId && e.Assunto.Contains("NFS")));
        }
        finally
        {
            await LimparAsync(cnpjs);
        }
    }

    private static async Task AssertNaoPassaAsync(Task<HttpResponseMessage> requisicao, string acao)
    {
        var resposta = await requisicao;
        Assert.False(resposta.IsSuccessStatusCode, $"B conseguiu {acao}: {(int)resposta.StatusCode}.");
        // Tem que ser o "não encontrado" do filtro de conta: 400/403 (CSRF,
        // permissão) ou 422 (validação) esconderiam se o filtro funcionou.
        Assert.True(resposta.StatusCode == HttpStatusCode.NotFound,
            $"{acao}: esperado 404, veio {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}");
    }

    // ---------- Apoio ----------

    /// <summary>Conta de perfil Empresa criada pelo cadastro de verdade (já logada) e o token anti-CSRF da sessão.</summary>
    private async Task<Conta> CriarContaAsync(List<string> cnpjs)
    {
        var cnpj = GerarCnpj("3");
        cnpjs.Add(cnpj);
        var cliente = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

        await AtualizarCsrfAsync(cliente, "/Account/Registrar");
        var registro = await cliente.PostAsJsonAsync("/api/auth/registrar", new
        {
            perfil = 1, documento = cnpj, nome = "Conta " + cnpj, email = $"iso{cnpj}@teste.com", senha = "Senha@Forte123", aceitouTermos = true
        });
        Assert.Equal(HttpStatusCode.OK, registro.StatusCode);

        // O token anti-CSRF é amarrado ao usuário: renova depois do login.
        await AtualizarCsrfAsync(cliente, "/api/empresas");

        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Documento == cnpj);
        var empresa = await db.Empresas.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.TenantId == tenant.Id);
        var usuario = await db.Users.AsNoTracking().SingleAsync(u => u.TenantId == tenant.Id);
        return new Conta(cliente, cnpj, tenant.Id, empresa.Id, usuario.GrupoId!.Value, usuario.Id);
    }

    private static async Task AtualizarCsrfAsync(HttpClient cliente, string url)
    {
        var resposta = await cliente.GetAsync(url);
        var cookie = resposta.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("NfseSaaS.Xsrf-Token="));
        var token = Uri.UnescapeDataString(cookie.Split(';')[0]["NfseSaaS.Xsrf-Token=".Length..]);
        cliente.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        cliente.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }

    /// <summary>Cliente, serviço, contrato e uma nota autorizada da conta A, gravados direto no banco.</summary>
    private async Task<DadosA> CriarDadosAsync(Conta a)
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantIdOverrideDeSistema = a.TenantId;

        var cliente = new Cliente
        {
            EmpresaId = a.EmpresaId, CpfCnpj = "11144477735", Nome = "Cliente da conta A", Apelido = "ClienteA",
            CodigoMunicipio = "2927408", Cep = "40000000", Logradouro = "Rua A", Numero = "1", Bairro = "Centro", Uf = "BA"
        };
        var servico = new Servico { EmpresaId = a.EmpresaId, Descricao = "Serviço A", CodigoTributacaoNacional = "010101", CodigoNbs = "115022000", ValorPadrao = 100m };
        db.Clientes.Add(cliente);
        db.Servicos.Add(servico);
        var contrato = new Contrato
        {
            EmpresaId = a.EmpresaId, ClienteId = cliente.Id, Descricao = "Contrato A", ValorAtual = 100m,
            DataInicioContrato = new DateOnly(2026, 1, 1), PeriodicidadeReajusteMeses = 12,
            Status = StatusContrato.Ativo, TipoCobranca = TipoCobrancaContrato.Mensal
        };
        db.Contratos.Add(contrato);
        db.ContratoServicos.Add(new ContratoServico { ContratoId = contrato.Id, ServicoId = servico.Id, Quantidade = 1, ValorUnitario = 100m });
        var nota = new Nfse
        {
            EmpresaId = a.EmpresaId, ClienteId = cliente.Id, ServicoId = servico.Id, ContratoId = contrato.Id,
            NumeroDps = 900_000 + Random.Shared.Next(99_999), SerieDps = "900", NumeroNfse = "123",
            ChaveAcesso = "CHAVE" + Guid.NewGuid().ToString("N"), DataCompetencia = new DateOnly(2026, 11, 1),
            DataEmissao = DateTimeOffset.UtcNow, ValorServico = 100m, DescricaoServico = "Serviço A",
            CodigoTributacaoNacional = "010101", CodigoNbs = "115022000", TribIssqn = "1", TpRetIssqn = "1",
            CstPisCofins = "00", TpRetPisCofins = "1", Status = NfseStatus.Autorizada, TipoAmbiente = TipoAmbiente.Homologacao,
            XmlNfse = "<NFSe xmlns=\"http://www.sped.fazenda.gov.br/nfse\"><infNFSe Id=\"NFS123\"/></NFSe>"
        };
        db.NotasFiscais.Add(nota);
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;
        return new DadosA(cliente.Id, servico.Id, contrato.Id, nota.Id);
    }

    private sealed record DadosExtras(Guid ModeloId, Guid EmailId, Guid DocumentoId);

    /// <summary>Modelo de e-mail, e-mail enviado e documento da empresa da conta A (SEG1).</summary>
    private async Task<DadosExtras> CriarDadosExtrasAsync(Conta a, DadosA dados)
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantIdOverrideDeSistema = a.TenantId;

        var modelo = new ModeloEmailNota { Nome = "Modelo da conta A", Assunto = "Nota [Número da nota]", Mensagem = "Olá", Padrao = false };
        db.ModelosEmailNota.Add(modelo);
        var email = new EmailLog
        {
            Tipo = "NfseTomador", Destinatario = "emaildacontaa@teste.com", Assunto = "Nota da conta A", CorpoHtml = "x",
            Status = StatusEmailLog.Enviado, EnviadoEm = DateTimeOffset.UtcNow, NfseId = dados.NfseId
        };
        db.EmailLogs.Add(email);
        var documento = new DocumentoEmpresa
        {
            EmpresaId = a.EmpresaId, Tipo = 1, NomeChave = string.Empty, DataEmissao = new DateOnly(2026, 11, 1),
            ValidaAte = new DateOnly(2027, 5, 1), NomeArquivo = "cnd.pdf", TamanhoBytes = 10
        };
        db.DocumentosEmpresa.Add(documento);
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;
        return new DadosExtras(modelo.Id, email.Id, documento.Id);
    }

    private async Task LimparAsync(IReadOnlyList<string> cnpjs)
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await db.Tenants.Where(t => cnpjs.Contains(t.Documento)).Select(t => t.Id).ToListAsync();
        await db.NfseEventos.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.NotasFiscais.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.MovimentacoesValorContrato.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.ContratoServicos.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Contratos.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Servicos.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Clientes.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.EmailLogs.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.ModelosEmailNota.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.DocumentosEmpresa.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.AceitesTermos.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.ContadoresDps.Where(x => db.Empresas.IgnoreQueryFilters().Any(e => e.Id == x.EmpresaId && ids.Contains(e.TenantId))).ExecuteDeleteAsync();
        await db.Empresas.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Users.Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.GrupoTelas.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Grupos.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.TestesGratisCnpj.Where(x => cnpjs.Contains(x.Cnpj)).ExecuteDeleteAsync();
        await db.Assinaturas.Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Tenants.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
    }

    /// <summary>CNPJ válido (DV certo) começando com o prefixo, sem colisão entre execuções.</summary>
    private static string GerarCnpj(string prefixo)
    {
        var aleatorio = string.Concat(Enumerable.Range(0, 12 - prefixo.Length).Select(_ => Random.Shared.Next(10)));
        var baseCnpj = prefixo + aleatorio;
        int Digito(string b, int[] pesos)
        {
            var resto = b.Select((c, i) => (c - '0') * pesos[i]).Sum() % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
        var d1 = Digito(baseCnpj, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var d2 = Digito(baseCnpj + d1, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return baseCnpj + d1 + d2;
    }
}

/// <summary>
/// Trava estrutural do L9: toda tabela com TenantId tem o filtro de conta,
/// ou está na lista de exceções com o motivo. Entidade nova com TenantId e
/// sem ITenantEntity quebra este teste em vez de vazar dados.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class FiltroDeContaNoModeloTests
{
    /// <summary>Com TenantId, mas lidas por quem não tem conta resolvida (webhook, jobs, login) e sempre filtradas à mão.</summary>
    private static readonly Dictionary<string, string> SemFiltroDeProposito = new()
    {
        ["Assinatura"] = "cobrança: webhook do Asaas e jobs, sem usuário logado; sempre por TenantId",
        ["EventoCobranca"] = "webhook do Asaas, antes de saber a conta",
        ["PremioIndicacao"] = "envolve duas contas (quem indicou e a indicada)",
        ["TesteGratisCnpj"] = "registro entre contas (um teste por CNPJ)",
        ["ReajusteAssinatura"] = "reajuste de assinantes: o operador agenda para várias contas e o job aplica, sem usuário da conta; sempre por TenantId/AssinaturaId",
        ["NotaMensalidade"] = "nota da conta emissora para a conta que pagou (TenantId = a pagadora); só o job da mensalidade usa",
        ["ExcecaoRecursoConta"] = "PL1: o operador da plataforma cria para qualquer conta; leitura sempre por TenantId (IRecursosConta)",
        ["ApplicationUser"] = "Identity/login antes de existir conta resolvida; consultas sempre por TenantId ou GrupoId"
    };

    private readonly AplicacaoTesteFactory _factory;

    public FiltroDeContaNoModeloTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Toda_entidade_com_TenantId_tem_o_filtro_de_conta_ou_uma_excecao_explicada()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();

        var semFiltro = new List<string>();
        foreach (var entidade in db.Model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            if (entidade.FindProperty("TenantId") is null)
                continue;

            var nome = entidade.ClrType.Name;
            var temFiltro = entidade.GetDeclaredQueryFilters().Any();
            var ehDeConta = typeof(NfseSaaS.Domain.Common.ITenantEntity).IsAssignableFrom(entidade.ClrType);

            if (ehDeConta && !temFiltro)
                semFiltro.Add($"{nome}: é ITenantEntity mas está sem filtro");
            else if (!ehDeConta && !SemFiltroDeProposito.ContainsKey(nome))
                semFiltro.Add($"{nome}: tem TenantId e não é ITenantEntity (implemente ITenantEntity ou explique em SemFiltroDeProposito)");
        }

        Assert.True(semFiltro.Count == 0, string.Join(Environment.NewLine, semFiltro));
    }
}
