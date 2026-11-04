using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Legal;
using Xunit;

namespace NfseSaaS.Tests;

public class DocumentosLegaisTests
{
    [Fact]
    public void Versoes_cabem_na_coluna_do_aceite()
    {
        // aceites_termos.VersaoTermosDeUso / VersaoPoliticaPrivacidade: varchar(20)
        Assert.InRange(DocumentosLegais.VersaoTermosDeUso.Length, 1, 20);
        Assert.InRange(DocumentosLegais.VersaoPoliticaPrivacidade.Length, 1, 20);
    }

    [Fact]
    public void Email_de_privacidade_e_do_dominio_do_servico()
    {
        Assert.EndsWith("@" + DocumentosLegais.Site, DocumentosLegais.EmailPrivacidade);
    }

    [Fact]
    public void Origens_do_aceite_cabem_na_coluna()
    {
        // aceites_termos.Origem: varchar(30)
        foreach (var origem in new[] { OrigemAceiteTermos.Cadastro, OrigemAceiteTermos.Convite, OrigemAceiteTermos.Atualizacao })
            Assert.InRange(origem.Length, 1, 30);
    }

    [Fact]
    public void Aceite_novo_ja_nasce_com_data()
    {
        var aceite = new AceiteTermos();

        Assert.NotEqual(Guid.Empty, aceite.Id);
        Assert.True(aceite.AceitoEm > DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}
