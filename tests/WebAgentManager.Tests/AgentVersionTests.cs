using WebAgentManager;
using Xunit;

namespace WebAgentManager.Tests;

public sealed class AgentVersionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "wam-version-" + Guid.NewGuid().ToString("N"));
    private string Executable => Path.Combine(root, "web-agent.exe");

    private void Manifest(string json)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "package.json"), json);
    }

    [Fact]
    public void PacoteTemPrioridadeSobreMetadadoGenerico()
    {
        Manifest("{\"name\":\"web-agent\",\"version\":\"1.1.1\"}");
        Assert.Equal("1.1.1", AgentVersion.Resolve(Executable, "1, 0, 0, 1"));
    }

    [Fact]
    public void SemPacotePreservaMetadadoDottedIdentificavel()
    {
        Assert.Equal("1.0.25", AgentVersion.Resolve(Executable, "1.0.25"));
    }

    [Theory]
    [InlineData("{\"name\":\"outro-programa\",\"version\":\"1.1.1\"}")]
    [InlineData("{\"name\":\"web-agent\",\"version\":\"1, 0, 0, 1\"}")]
    [InlineData("{\"name\":\"web-agent\"}")]
    [InlineData("arquivo invalido")]
    public void RecusaManifestoInvalidoSemInventarVersao(string json)
    {
        Manifest(json);
        Assert.Throws<ArgumentException>(() => AgentVersion.Resolve(Executable, "1, 0, 0, 1"));
    }

    [Fact]
    public void SemPacoteNaoNormalizaMetadadoGenericoComoVersaoReal()
    {
        Assert.Throws<ArgumentException>(() => AgentVersion.Resolve(Executable, "1, 0, 0, 1"));
    }

    [Fact]
    public void RecusaManifestoExcessivamenteGrande()
    {
        Manifest(new string(' ', 65537));
        Assert.Throws<ArgumentException>(() => AgentVersion.Resolve(Executable, "1.1.1"));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
