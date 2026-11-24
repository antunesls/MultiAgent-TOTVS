using WebAgentManager;
using Xunit;

namespace WebAgentManager.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "wam-tests-" + Guid.NewGuid().ToString("N"));
    private Store NewStore() => new(root);
    private static EnvironmentEntry Valid(string agent) => new() { Client = "Cliente A", Name = "Produção", Kind = "Produção", Url = "https://cliente/webapp/", AgentId = agent };

    [Fact]
    public void PersisteCadastroEEditaSemDuplicar()
    {
        using var store = NewStore();
        store.AddAgent(new("agente", "Teste", "1.1.1", "x64", "unused", "hash"));
        var saved = store.SaveEnvironment(Valid("agente"));
        store.SaveEnvironment(saved with { Name = "Homologação" });
        Assert.Equal("Homologação", Assert.Single(store.Environments()).Name);
        using var reopened = NewStore();
        Assert.Equal(saved.Id, Assert.Single(reopened.Environments()).Id);
    }

    [Fact]
    public void RecusaAgenteNaoCadastrado()
    {
        using var store = NewStore();
        Assert.Throws<ArgumentException>(() => store.SaveEnvironment(Valid("ausente")));
        Assert.Empty(store.Environments());
    }

    [Fact]
    public void ImportacaoInvalidaNaoAlteraCadastros()
    {
        using var store = NewStore();
        store.AddAgent(new("agente", "Teste", "1.1.1", "x64", "unused", "hash"));
        var good = store.SaveEnvironment(Valid("agente"));
        var import = new Catalog(1, [good with { Name = "Alterado" }, good with { Id = Guid.NewGuid().ToString("N"), AgentId = "ausente" }], new());
        Assert.Throws<ArgumentException>(() => store.Import(import));
        Assert.Equal(good, Assert.Single(store.Environments()));
    }

    [Fact]
    public void NormalizaIdentificadorDoAmbiente()
    {
        using var store = NewStore();
        store.AddAgent(new("agente", "Teste", "1.1.1", "x64", "unused", "hash"));
        var saved = store.SaveEnvironment(Valid("agente") with { Id = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" });
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", saved.Id);
        store.SaveEnvironment(saved with { Name = "Alterado" });
        Assert.Single(store.Environments());
    }

    [Fact]
    public void ImportacaoRecusaColisaoDeCaixaAntesDeGravar()
    {
        using var store = NewStore();
        store.AddAgent(new("agente", "Teste", "1.1.1", "x64", "unused", "hash"));
        var entry = Valid("agente") with { Id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" };
        Assert.Throws<ArgumentException>(() => store.Import(new Catalog(1, [entry, entry with { Id = entry.Id.ToUpperInvariant() }], new())));
        Assert.Empty(store.Environments());
    }

    [Fact]
    public void LimiteDeCadastrosAlinhaCriacaoEImportacao()
    {
        using var store = NewStore();
        store.AddAgent(new("agente", "Teste", "1.1.1", "x64", "unused", "hash"));
        var entries = Enumerable.Range(0, 2000).Select(_ => Valid("agente") with { Id = Guid.NewGuid().ToString("N"), Notes = new string('x', 4000) }).ToList();
        store.Import(new(1, entries, new()));
        Assert.Throws<ArgumentException>(() => store.SaveEnvironment(Valid("agente")));
        store.SaveEnvironment(entries[0] with { Name = "Editado" });
        var exported = store.Export();
        Assert.True(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(exported).Length > 2 * 1024 * 1024);
        store.Import(exported);
        Assert.Equal(2000, store.Environments().Count);
    }

    [Fact]
    public void ExportaImportaEExcluiSemApagarAgente()
    {
        using var store = NewStore();
        store.AddAgent(new("agente", "Teste", "1.1.1", "x64", "unused", "hash"));
        var saved = store.SaveEnvironment(Valid("agente"));
        var catalog = store.Export();
        store.DeleteEnvironment(saved.Id);
        store.Import(catalog);
        Assert.Equal(saved, Assert.Single(store.Environments()));
        Assert.Single(store.Agents());
        Assert.NotEmpty(store.History());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
