using WebAgentManager;
using Xunit;

namespace WebAgentManager.Tests;

public class DomainTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8080/webapp/")]
    [InlineData("https://cliente.exemplo/webapp/?E=teste&P=SIGAFAT")]
    public void AceitaUrlWebApp(string url) => Assert.Equal(url, Rules.WebUrl(url));

    [Theory]
    [InlineData("file:///C:/teste")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://usuario:senha@servidor/")]
    [InlineData("http://servidor:99999/")]
    [InlineData("http://servidor/\n--help")]
    public void RecusaUrlInsegura(string url) => Assert.Throws<ArgumentException>(() => Rules.WebUrl(url));

    [Fact]
    public void LaunchUsaArgumentosSeparadosSemShell()
    {
        var info = Rules.LaunchInfo(@"C:\Agentes\cliente A\web-agent.exe", "https://servidor:443/webapp/?E=a&P=b", @"C:\Program Files\Edge\msedge.exe");
        Assert.False(info.UseShellExecute);
        Assert.Equal("launch", info.ArgumentList[0]);
        Assert.Equal("https://servidor:443/webapp/?E=a&P=b", info.ArgumentList[1]);
        Assert.Equal(@"--browser=C:\Program Files\Edge\msedge.exe", info.ArgumentList[2]);
    }

    [Fact]
    public void NavegadorPadraoOmiteParametro()
    {
        Assert.Equal(2, Rules.LaunchInfo(@"C:\web-agent.exe", "http://localhost/", "").ArgumentList.Count);
    }

    [Fact]
    public void AmbienteExigeClienteNomeEAgente()
    {
        Assert.Throws<ArgumentException>(() => Rules.Environment(new EnvironmentEntry()));
    }

    [Theory]
    [InlineData("1.0.15", @"C:\Program Files\Edge\msedge.exe")]
    [InlineData("1.0.23", "")]
    [InlineData("Não identificada", "")]
    public void RecusaVersaoIncompativelComModoDeAbertura(string version, string browser) => Assert.Throws<ArgumentException>(() => Rules.LaunchSupported(version, browser));

    [Theory]
    [InlineData("1.0.24", "")]
    [InlineData("1.0.16", @"C:\Program Files\Edge\msedge.exe")]
    [InlineData("v1.1.1 - x64", "")]
    public void AceitaVersaoCompativel(string version, string browser) => Rules.LaunchSupported(version, browser);

    [Theory]
    [InlineData("CN=TOTVS S.A., O=TOTVS S.A., C=BR", true)]
    [InlineData("CN=TOTVS S.A., O=Outra empresa, C=BR", false)]
    [InlineData("CN=NOTTOTVS, O=NOTTOTVS, C=BR", false)]
    [InlineData("CN=TOTVS S.A.", false)]
    public void ConfereOrganizacaoDoCertificadoSemSubstring(string subject, bool expected) => Assert.Equal(expected, Rules.TotvsSigner(subject));
}
