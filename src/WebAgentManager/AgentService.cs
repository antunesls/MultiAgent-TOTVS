using System.Diagnostics;

namespace WebAgentManager;

public static class SafeFiles
{
    public static void NoLinks(string path)
    {
        for (var info = new DirectoryInfo(Path.GetFullPath(path)); info is not null; info = info.Parent)
            if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Links simbólicos e junctions não são permitidos nas pastas dos agentes.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Arquivo é um link simbólico.");
    }

    public static void CopyNew(string source, string destination)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        if (!Directory.Exists(source)) throw new ArgumentException("Pasta de origem não encontrada.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new ArgumentException("A pasta de destino já existe; nenhuma substituição foi realizada.");
        if (destination.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("O destino não pode estar dentro da origem.");
        NoLinks(source);
        NoLinks(Path.GetDirectoryName(destination)!);
        var entries = new List<(string Path, bool Directory)>();
        Scan(source, entries);
        var staging = destination + ".preparing-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var item in entries)
            {
                NoLinks(item.Path);
                var target = Path.Combine(staging, Path.GetRelativePath(source, item.Path));
                if (item.Directory) Directory.CreateDirectory(target);
                else File.Copy(item.Path, target, false);
            }
            Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static void Scan(string root, List<(string Path, bool Directory)> entries)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("A origem contém links simbólicos/junctions. Remova-os antes de importar.");
            var isDirectory = Directory.Exists(path);
            entries.Add((path, isDirectory));
            if (isDirectory) Scan(path, entries);
        }
    }
}

public sealed class AgentService(Store store, IPlatform platform) : IDisposable
{
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly Dictionary<string, (PreparationEntry Entry, IRunningProcess? Process)> preparations = [];
    private readonly List<(SessionEntry Entry, IRunningProcess Process)> sessions = [];
    private readonly object gate = new();

    public List<SessionEntry> Sessions()
    {
        lock (gate) return sessions.Select(session =>
        {
            var running = !session.Process.HasExited;
            if (!running) session.Process.Dispose();
            return session.Entry with { Running = running };
        }).ToList();
    }
    public List<PreparationEntry> Preparations()
    {
        lock (gate) return preparations.Values.Select(item => item.Entry with { Stage = item.Process is not null && item.Process.HasExited && item.Entry.Stage == "Instalando" ? $"Aguardando captura (saída {item.Process.ExitCode})" : item.Entry.Stage }).ToList();
    }

    public async Task<AgentEntry> ImportFolder(string name, string source)
    {
        await operation.WaitAsync();
        try
        {
            lock (gate)
                if (preparations.Values.Any(item => item.Entry.Stage == "Instalando"))
                    throw new ArgumentException("Conclua ou cancele a preparação anterior antes de importar uma pasta.");
            return await Capture(name, source);
        }
        finally { operation.Release(); }
    }

    private async Task<AgentEntry> Capture(string name, string source)
    {
        Rules.Text(name, "Nome do WebAgent", 120);
        if (platform.HasAnyAgent()) throw new ArgumentException("Feche os WebAgents pela bandeja antes de copiar a instalação. Nenhum processo será encerrado pelo gerenciador.");
        var executable = Path.Combine(source, "web-agent.exe");
        if (!File.Exists(executable)) throw new ArgumentException("A pasta selecionada não contém web-agent.exe.");
        SafeFiles.NoLinks(executable);
        var inspection = await platform.Inspect(executable);
        RequireTrusted(inspection);
        if (inspection.Architecture is not ("x86" or "x64")) throw new ArgumentException("Arquitetura não suportada. Selecione um WebAgent Windows x86 ou x64.");
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(store.Root, "agents", id);
        SafeFiles.CopyNew(source, directory);
        var copied = await platform.Inspect(Path.Combine(directory, "web-agent.exe"));
        RequireTrusted(copied);
        if (copied.Sha256 != inspection.Sha256) throw new ArgumentException("O executável mudou durante a cópia. Repita a preparação.");
        var entry = new AgentEntry(id, name, inspection.Version, inspection.Architecture, directory, inspection.Sha256);
        store.AddAgent(entry);
        store.Log("WebAgent preparado", $"{name}: {entry.Version} {entry.Architecture}");
        return entry;
    }

    public async Task<PreparationEntry> RegisterInstaller(string name, string installer)
    {
        Rules.Text(name, "Nome do WebAgent", 120);
        if (!new[] { ".exe", ".msi" }.Contains(Path.GetExtension(installer).ToLowerInvariant())) throw new ArgumentException("Selecione um instalador .exe ou .msi.");
        SafeFiles.NoLinks(installer);
        var inspection = await platform.Inspect(installer);
        RequireTrusted(inspection);
        var entry = new PreparationEntry(Guid.NewGuid().ToString("N"), name, installer, inspection, "Verificado");
        lock (gate) preparations.Add(entry.Id, (entry, null));
        store.Log("Instalador verificado", $"{name}. SHA-256: {inspection.Sha256}");
        return entry;
    }

    private static void RequireTrusted(FileInspection inspection)
    {
        if (!inspection.Trusted) throw new ArgumentException($"Arquivo recusado: assinatura TOTVS válida exigida. Resultado: {inspection.Status}. Obtenha o instalador oficial e confira os certificados do Windows.");
    }

    public async Task<AgentEntry> RefreshAgent(string id)
    {
        await operation.WaitAsync();
        try
        {
            var agent = store.Agents().FirstOrDefault(item => item.Id == id) ?? throw new ArgumentException("WebAgent não encontrado.");
            var executable = Path.Combine(agent.Directory, "web-agent.exe");
            SafeFiles.NoLinks(executable);
            var inspection = await platform.Inspect(executable);
            RequireTrusted(inspection);
            if (inspection.Sha256 != agent.Sha256) throw new ArgumentException("O executável mudou desde a captura. Prepare uma nova versão; atualizar identificação não substitui arquivos nem aceita alterações do executável.");
            if (inspection.Architecture is not ("x86" or "x64")) throw new ArgumentException("Arquitetura não suportada.");
            var updated = agent with { Version = inspection.Version, Architecture = inspection.Architecture };
            store.AddAgent(updated);
            store.Log("Identificação atualizada", $"{agent.Name}: {agent.Version} → {updated.Version} {updated.Architecture}. Arquivos e vínculos preservados.");
            return updated;
        }
        finally { operation.Release(); }
    }

    private (PreparationEntry Entry, IRunningProcess? Process) Preparation(string id)
    {
        lock (gate) return preparations.TryGetValue(id, out var item) ? item : throw new ArgumentException("Preparação não encontrada. Após reiniciar, importe a pasta instalada ou selecione o instalador novamente.");
    }

    public async Task<PreparationEntry> Install(string id, bool consent)
    {
        await operation.WaitAsync();
        try
        {
            if (!consent) throw new ArgumentException("Confirme que a instalação pode alterar a pasta padrão do WebAgent.");
            var item = Preparation(id);
            if (item.Entry.Stage != "Verificado") throw new ArgumentException("Esse instalador já foi aberto. Capture a pasta instalada ou cancele a preparação.");
            lock (gate)
                if (preparations.Values.Any(value => value.Entry.Id != id && value.Entry.Stage == "Instalando"))
                    throw new ArgumentException("Conclua ou cancele a preparação anterior antes de instalar outro WebAgent.");
            if (platform.HasAnyAgent()) throw new ArgumentException("Há WebAgents abertos. Feche-os antes de instalar uma versão. Os ambientes gerenciados não serão encerrados automaticamente.");
            var inspection = await platform.Inspect(item.Entry.FileName);
            RequireTrusted(inspection);
            if (inspection.Sha256 != item.Entry.Inspection.Sha256) throw new ArgumentException("O instalador foi alterado. Importe novamente.");
            string? backup = null;
            if (Directory.Exists(platform.DefaultAgentDirectory))
            {
                backup = Path.Combine(store.Root, "backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                SafeFiles.CopyNew(platform.DefaultAgentDirectory, backup);
                store.Log("Backup pré-instalação", backup);
            }
            if (platform.HasAnyAgent()) throw new ArgumentException("Um WebAgent foi aberto durante o backup. Instalação cancelada.");
            var isMsi = item.Entry.FileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
            var start = new ProcessStartInfo(isMsi ? "msiexec.exe" : item.Entry.FileName) { UseShellExecute = true };
            if (isMsi) { start.ArgumentList.Add("/i"); start.ArgumentList.Add(item.Entry.FileName); }
            var process = platform.Start(start);
            var entry = item.Entry with { Stage = "Instalando", Backup = backup, InstallerPid = process.Id };
            lock (gate) preparations[id] = (entry, process);
            store.Log("Instalador aberto", item.Entry.Name + ". Conclua a instalação e feche o agente antes da captura.");
            return entry;
        }
        finally { operation.Release(); }
    }

    public async Task<AgentEntry> CaptureInstalled(string id)
    {
        await operation.WaitAsync();
        try
        {
            var item = Preparation(id);
            if (item.Entry.Stage != "Instalando") throw new ArgumentException("Esta preparação não está aguardando captura.");
            if (item.Process is null || !item.Process.HasExited) throw new ArgumentException("Conclua ou feche o instalador antes de capturar a pasta instalada.");
            if (item.Process.ExitCode is not (0 or 3010 or 1641)) throw new ArgumentException($"O instalador terminou com código {item.Process.ExitCode}. Cancele a preparação e confira a instalação antes de importar uma pasta manualmente.");
            var agent = await Capture(item.Entry.Name, platform.DefaultAgentDirectory);
            item.Process.Dispose();
            lock (gate) preparations[id] = (item.Entry with { Stage = "Concluído" }, null);
            return agent;
        }
        finally { operation.Release(); }
    }

    public async Task CancelPreparation(string id)
    {
        await operation.WaitAsync();
        try
        {
            var item = Preparation(id);
            if (item.Process is not null && !item.Process.HasExited) throw new ArgumentException("Feche o instalador antes de cancelar a preparação.");
            item.Process?.Dispose();
            lock (gate) preparations[id] = (item.Entry with { Stage = "Cancelado" }, null);
            store.Log("Preparação cancelada", item.Entry.Name + ". Backups e arquivos existentes foram preservados.");
        }
        finally { operation.Release(); }
    }

    public async Task<SessionEntry> Launch(string environmentId)
    {
        await operation.WaitAsync();
        try
        {
            lock (gate)
                if (preparations.Values.Any(item => item.Entry.Stage == "Instalando"))
                    throw new ArgumentException("Conclua ou cancele a preparação em andamento antes de abrir ambientes.");
            var environment = store.Environments().FirstOrDefault(entry => entry.Id == environmentId) ?? throw new ArgumentException("Ambiente não encontrado.");
            var agent = store.Agents().First(entry => entry.Id == environment.AgentId);
            var directory = Path.Combine(store.Root, "instances", environment.Id, agent.Id);
            if (!Directory.Exists(directory)) SafeFiles.CopyNew(agent.Directory, directory);
            var executable = Path.Combine(directory, "web-agent.exe");
            SafeFiles.NoLinks(executable);
            var inspection = await platform.Inspect(executable);
            RequireTrusted(inspection);
            if (inspection.Sha256 != agent.Sha256) throw new ArgumentException("O executável desta instância mudou, possivelmente por atualização do servidor. Prepare a nova versão explicitamente e atualize o vínculo do ambiente.");
            var browser = environment.Browser == "" ? store.Settings().Browser : environment.Browser;
            Rules.LaunchSupported(inspection.Version, browser);
            if (browser != "" && !File.Exists(browser)) throw new ArgumentException("Navegador não encontrado. Corrija o caminho no cadastro ou nas configurações.");
            var process = platform.Start(Rules.LaunchInfo(executable, environment.Url, browser));
            var session = new SessionEntry(Guid.NewGuid().ToString("N"), environment.Id, environment.Client + " / " + environment.Name, process.Id, DateTimeOffset.Now.ToString("O"), true);
            lock (gate)
            {
                foreach (var item in sessions.Where(item => item.Process.HasExited)) item.Process.Dispose();
                sessions.RemoveAll(item => item.Process.HasExited);
                sessions.Add((session, process));
            }
            store.Log("Ambiente aberto", $"{session.Name}. WebAgent {agent.Version} {agent.Architecture}, PID {process.Id}. Confirme a conexão no WebApp.");
            return session;
        }
        finally { operation.Release(); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var item in sessions) item.Process.Dispose();
            foreach (var item in preparations.Values) item.Process?.Dispose();
        }
        operation.Dispose();
    }
}
