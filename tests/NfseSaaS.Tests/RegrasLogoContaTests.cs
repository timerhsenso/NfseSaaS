using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Aparencia;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>B9: logo da conta — tipo pelo conteúdo, tamanho e plano.</summary>
public class RegrasLogoContaTests
{
    [Fact]
    public void Reconhece_png_jpeg_e_webp_pelo_conteudo()
    {
        Assert.Equal("image/png", RegrasLogoConta.DetectarTipo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        Assert.Equal("image/jpeg", RegrasLogoConta.DetectarTipo(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }));
        Assert.Equal("image/webp", RegrasLogoConta.DetectarTipo("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Null(RegrasLogoConta.DetectarTipo("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8));
        Assert.Null(RegrasLogoConta.DetectarTipo("GIF89a"u8));
        Assert.Null(RegrasLogoConta.DetectarTipo(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Recusa_vazio_grande_e_formato_errado()
    {
        Assert.Throws<RegraNegocioException>(() => RegrasLogoConta.Validar(null));
        Assert.Throws<RegraNegocioException>(() => RegrasLogoConta.Validar(Array.Empty<byte>()));
        var grande = new byte[RegrasLogoConta.TamanhoMaximoBytes + 1];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(grande, 0);
        Assert.Contains("512 KB", Assert.Throws<RegraNegocioException>(() => RegrasLogoConta.Validar(grande)).Message);
        Assert.Throws<RegraNegocioException>(() => RegrasLogoConta.Validar("GIF89a"u8.ToArray()));
    }

    [Fact]
    public void Versao_muda_com_o_conteudo()
    {
        var a = RegrasLogoConta.Versao(new byte[] { 1, 2, 3 });
        Assert.Equal(16, a.Length);
        Assert.Equal(a, RegrasLogoConta.Versao(new byte[] { 1, 2, 3 }));
        Assert.NotEqual(a, RegrasLogoConta.Versao(new byte[] { 1, 2, 4 }));
    }

    [Fact]
    public void Disponivel_quando_o_plano_libera_ou_para_o_operador()
    {
        Assert.True(RegrasLogoConta.Disponivel(liberadaNoPlano: true, operadorPlataforma: false));
        Assert.False(RegrasLogoConta.Disponivel(liberadaNoPlano: false, operadorPlataforma: false));
        Assert.True(RegrasLogoConta.Disponivel(liberadaNoPlano: false, operadorPlataforma: true));
    }
}
