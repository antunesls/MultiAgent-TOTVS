using System.Diagnostics;
using WebAgentManager;
using Xunit;

namespace WebAgentManager.Tests;

public sealed class AgentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "wam-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakePlatform platform = new();

    private (Store, AgentService, string) Setup()
    {
        var store = new Store(root);
        var service = new AgentService(store, platform);
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "web-agent.exe"), "teste");
        File.WriteAllText(Path.Combine(source, "cliente.dll"), "original");
        return (store, service, source);
    }

    [Fact]
    public async Task CopiaPreservaOrigemEIsolaAmbientes()
    {
        var (store, service, source) = Setup();
        using (store)
        {
            var agent = await service.ImportFolder("Agente teste", source);
            var first = store.SaveEnvironment(new() { Client = "A", Name = "HML", Url = "http://cliente/", AgentId = agent.Id });
            var second = store.SaveEnvironment(first with { Id = "", Client = "B" });
            await service.Launch(first.Id);
            await service.Launch(second.Id);
            Assert.Equal(2, platform.Starts.Count);
            Assert.NotEqual(platform.Starts[0].WorkingDirectory, platform.Starts[1].WorkingDirectory);
            Assert.NotEqual(source, agent.Directory);
            Assert.Equal("original", File.ReadAllText(Path.Combine(source, "cliente.dll")));
            Assert.Equal(2, service.Sessions().Count);
        }
    }

    [Fact]
    public async Task AssinaturaInvalidaImpedeImportacao()
    {
        var (store, service, source) = Setup();
        using (store)
        {
            platform.Trusted = false;
            await Assert.ThrowsAsync<ArgumentException>(() => service.ImportFolder("Teste", source));
            Assert.Empty(store.Agents());
        }
    }

    [Fact]
    public async Task PreparacaoRecusaInstalarComAgentesAbertos()
    {
        var (store, service, _) = Setup();
        using (store)
        {
            var installer = Path.Combine(root, "setup.exe");
            File.WriteAllText(installer, "pacote");
            var entry = await service.RegisterInstaller("Teste", installer);
            platform.AnyAgent = true;
            await Assert.ThrowsAsync<ArgumentException>(() => service.Install(entry.Id, true));
            Assert.Empty(platform.Starts);
        }
    }

    [Fact]
    public async Task InstalacaoExigeConsentimentoEFazBackupAntesDeAbrir()
    {
        var (store, service, source) = Setup();
        using (store)
        {
            platform.DefaultAgentDirectory = source;
            var installer = Path.Combine(root, "setup.msi");
            File.WriteAllText(installer, "pacote");
            var entry = await service.RegisterInstaller("Teste", installer);
            await Assert.ThrowsAsync<ArgumentException>(() => service.Install(entry.Id, false));
            var started = await service.Install(entry.Id, true);
            Assert.NotNull(started.Backup);
            Assert.Equal("original", File.ReadAllText(Path.Combine(started.Backup!, "cliente.dll")));
            Assert.Equal("msiexec.exe", Path.GetFileName(Assert.Single(platform.Starts).FileName));
            Assert.Equal("original", File.ReadAllText(Path.Combine(source, "cliente.dll")));
        }
    }

    [Fact]
    public async Task AtualizaIdentificacaoSemReinstalarOuMudarIdentificador()
    {
        var (store, service, source) = Setup();
        using (store)
        {
            var agent = await service.ImportFolder("CPQ Produção", source);
            store.AddAgent(agent with { Version = "1, 0, 0, 1" });
            var environment = store.SaveEnvironment(new() { Client = "CPQ", Name = "Produção", Url = "https://cliente/", AgentId = agent.Id });
            platform.AnyAgent = true; // Leitura de metadados não deve exigir encerrar sessões.
            var updated = await service.RefreshAgent(agent.Id);
            Assert.Equal("1.1.1", updated.Version);
            Assert.Equal(agent.Id, updated.Id);
            Assert.Equal(agent.Directory, updated.Directory);
            Assert.Equal(agent.Sha256, updated.Sha256);
            Assert.Equal(updated, Assert.Single(store.Agents()));
            Assert.Equal(environment, Assert.Single(store.Environments()));
            Assert.Empty(platform.Starts);
        }
    }

    [Fact]
    public async Task AtualizacaoDeIdentificacaoNaoAceitaExecutavelAlterado()
    {
        var (store, service, source) = Setup();
        using (store)
        {
            var agent = await service.ImportFolder("Agente", source);
            platform.Hash = "alterado";
            await Assert.ThrowsAsync<ArgumentException>(() => service.RefreshAgent(agent.Id));
            Assert.Equal(agent, Assert.Single(store.Agents()));
        }
    }

    [Fact]
    public async Task LiberaRecursosDeSessaoEncerradaSemPerderEstadoFinal()
    {
        var (store, service, source) = Setup();
        using (store)
        {
            var agent = await service.ImportFolder("Agente", source);
            var environment = store.SaveEnvironment(new() { Client = "A", Name = "HML", Url = "http://cliente/", AgentId = agent.Id });
            await service.Launch(environment.Id);
            platform.LastProcess!.HasExited = true;
            Assert.False(Assert.Single(service.Sessions()).Running);
            Assert.True(platform.LastProcess.Disposed);
            Assert.False(Assert.Single(service.Sessions()).Running);
        }
    }

    [Fact]
    public async Task CapturaConcluidaRegistraAgenteEImpedeCapturaDuplicada()
    {
        var (store, service, source) = Setup();
        using (store)
        {
            platform.DefaultAgentDirectory = source;
            var installer = Path.Combine(root, "setup.exe");
            File.WriteAllText(installer, "pacote");
            var entry = await service.RegisterInstaller("Versao preparada", installer);
            await service.Install(entry.Id, true);
            await Assert.ThrowsAsync<ArgumentException>(() => service.CaptureInstalled(entry.Id));
            platform.LastProcess!.HasExited = true;
            var captured = await service.CaptureInstalled(entry.Id);
            Assert.Equal("Versao preparada", captured.Name);
            Assert.Equal("Concluído", Assert.Single(service.Preparations()).Stage);
            Assert.Single(store.Agents());
            Assert.True(platform.LastProcess.Disposed);
            Assert.NotEqual(source, captured.Directory);
            await Assert.ThrowsAsync<ArgumentException>(() => service.CaptureInstalled(entry.Id));
        }
    }

    [Fact]
    public async Task NuncaAbreSegundoInstaladorEnquantoPrimeiroEstaPendente()
    {
        var (store, service, _) = Setup();
        using (store)
        {
            var installer = Path.Combine(root, "setup.exe");
            File.WriteAllText(installer, "pacote");
            var first = await service.RegisterInstaller("A", installer);
            var second = await service.RegisterInstaller("B", installer);
            await service.Install(first.Id, true);
            await Assert.ThrowsAsync<ArgumentException>(() => service.Install(second.Id, true));
            await Assert.ThrowsAsync<ArgumentException>(() => service.CancelPreparation(first.Id));
            Assert.Single(platform.Starts);
        }
    }

    [Fact]
    public async Task AlteracaoDoHashDaInstanciaBloqueiaAbertura()
    {
        var (store, service, source) = Setup();
        using (store)
        {
            var agent = await service.ImportFolder("Agente", source);
            var environment = store.SaveEnvironment(new() { Client = "Cliente", Name = "HML", Url = "https://cliente/", AgentId = agent.Id });
            await service.Launch(environment.Id);
            platform.Hash = "outro-hash";
            await Assert.ThrowsAsync<ArgumentException>(() => service.Launch(environment.Id));
            Assert.Single(platform.Starts);
        }
    }

    [Fact]
    public void FalhaDeCopiaNaoDeixaDestinoParcial()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        var file = Path.Combine(source, "locked.dll");
        File.WriteAllText(file, "conteudo");
        using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var destination = Path.Combine(root, "copy");
        Assert.Throws<IOException>(() => SafeFiles.CopyNew(source, destination));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task CapturaRecusaInstalacaoCancelada()
    {
        var (store, service, source) = Setup();
        using (store)
        {
            platform.DefaultAgentDirectory = source;
            var installer = Path.Combine(root, "setup.exe");
            File.WriteAllText(installer, "pacote");
            var entry = await service.RegisterInstaller("Teste", installer);
            await service.Install(entry.Id, true);
            platform.LastProcess!.HasExited = true;
            platform.LastProcess.Result = 1602;
            await Assert.ThrowsAsync<ArgumentException>(() => service.CaptureInstalled(entry.Id));
            Assert.Empty(store.Agents());
        }
    }

    [Fact]
    public async Task ImportacaoDePastaBloqueadaEnquantoInstaladorEstaAberto()
    {
        var (store, service, source) = Setup();
        using (store)
        {
            var installer = Path.Combine(root, "setup.exe");
            File.WriteAllText(installer, "pacote");
            var entry = await service.RegisterInstaller("Teste", installer);
            await service.Install(entry.Id, true);
            await Assert.ThrowsAsync<ArgumentException>(() => service.ImportFolder("Outro", source));
        }
    }

    [Fact]
    public void CopiaRecusaReparsePoint()
    {
        Directory.CreateDirectory(root);
        // Junction criada apenas dentro do diretório temporário do teste.
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        var junction = Path.Combine(source, "link");
        using var process = Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", junction, target }, CreateNoWindow = true, RedirectStandardOutput = true });
        process!.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.Throws<ArgumentException>(() => SafeFiles.CopyNew(source, Path.Combine(root, "copy")));
        Directory.Delete(junction);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

public sealed class FakeProcess(int id) : IRunningProcess
{
    public int Id => id;
    public bool HasExited { get; set; }
    public int Result { get; set; }
    public int? ExitCode => HasExited ? Result : null;
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

public sealed class FakePlatform : IPlatform
{
    public bool Trusted { get; set; } = true;
    public bool AnyAgent { get; set; }
    public string Hash { get; set; } = "hash";
    public string DefaultAgentDirectory { get; set; } = "nonexistent";
    public List<ProcessStartInfo> Starts { get; } = [];
    public FakeProcess? LastProcess { get; private set; }
    public Task<FileInspection> Inspect(string path) => Task.FromResult(new FileInspection("1.1.1", "x64", Hash, Trusted, "TOTVS", "Valid"));
    public bool HasAnyAgent() => AnyAgent;
    public IRunningProcess Start(ProcessStartInfo info) { Starts.Add(info); return LastProcess = new FakeProcess(100 + Starts.Count); }
    public IReadOnlyList<BrowserEntry> Browsers() => [];
}
