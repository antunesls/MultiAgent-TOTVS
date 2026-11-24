using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace WebAgentManager;

public sealed class Store : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly object gate = new();
    public string Root { get; }
    public Store(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "manager.db"), Pooling = false }.ToString());
        connection.Open();
        Execute("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS environments(id TEXT PRIMARY KEY, payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS agents(id TEXT PRIMARY KEY, payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS settings(id TEXT PRIMARY KEY, payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS history(id INTEGER PRIMARY KEY AUTOINCREMENT, payload TEXT NOT NULL);");
    }

    private void Execute(string sql, params (string, object)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private List<T> Read<T>(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<T>(reader.GetString(0))!);
        return result;
    }

    private void Upsert<T>(string table, string id, T value) => Execute($"INSERT INTO {table}(id,payload) VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload", ("$id", id), ("$payload", JsonSerializer.Serialize(value)));

    public List<EnvironmentEntry> Environments() { lock (gate) return Read<EnvironmentEntry>("SELECT payload FROM environments ORDER BY id"); }
    public List<AgentEntry> Agents() { lock (gate) return Read<AgentEntry>("SELECT payload FROM agents ORDER BY id"); }
    public List<HistoryEntry> History() { lock (gate) return Read<HistoryEntry>("SELECT payload FROM history ORDER BY id DESC LIMIT 200"); }
    public ManagerSettings Settings() { lock (gate) return Read<ManagerSettings>("SELECT payload FROM settings WHERE id='main'").FirstOrDefault() ?? new(); }

    public void SaveSettings(ManagerSettings settings)
    {
        Rules.Browser(settings.Browser);
        lock (gate) Upsert("settings", "main", settings);
    }

    private void ValidateEnvironment(EnvironmentEntry entry)
    {
        Rules.Environment(entry);
        if (!Agents().Any(agent => agent.Id == entry.AgentId)) throw new ArgumentException("WebAgent não encontrado. Prepare ou importe o agente antes de vincular o ambiente.");
        if (entry.Id != "" && !Guid.TryParseExact(entry.Id, "N", out _)) throw new ArgumentException("Identificador do ambiente inválido.");
    }

    public EnvironmentEntry SaveEnvironment(EnvironmentEntry entry)
    {
        lock (gate)
        {
            ValidateEnvironment(entry);
            var saved = entry with { Id = entry.Id == "" ? Guid.NewGuid().ToString("N") : Rules.EnvironmentId(entry.Id) };
            var existing = Environments();
            if (!existing.Any(item => item.Id == saved.Id) && existing.Count >= Rules.MaxEnvironments)
                throw new ArgumentException($"Limite de {Rules.MaxEnvironments} ambientes atingido. Edite ou exclua um cadastro antes de criar outro.");
            Upsert("environments", saved.Id, saved);
            Log("Cadastro", $"{saved.Client} / {saved.Name}");
            return saved;
        }
    }

    public void DeleteEnvironment(string id)
    {
        id = Rules.EnvironmentId(id);
        lock (gate)
        {
            var entry = Environments().FirstOrDefault(item => item.Id == id) ?? throw new ArgumentException("Ambiente não encontrado.");
            Execute("DELETE FROM environments WHERE id=$id", ("$id", id));
            Log("Exclusão", $"{entry.Client} / {entry.Name}. Arquivos preservados.");
        }
    }

    public void AddAgent(AgentEntry entry) { lock (gate) Upsert("agents", entry.Id, entry); }
    public void Log(string action, string detail)
    {
        lock (gate)
        {
            Execute("INSERT INTO history(payload) VALUES($payload)", ("$payload", JsonSerializer.Serialize(new HistoryEntry(DateTimeOffset.Now.ToString("O"), action, detail))));
            Execute("DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY id DESC LIMIT 2000)");
        }
    }

    public Catalog Export() { lock (gate) return new(1, Environments(), Settings()); }
    public void Import(Catalog catalog)
    {
        lock (gate)
        {
            if (catalog.Schema != 1 || catalog.Environments is null || catalog.Settings is null || catalog.Environments.Count > 2000)
                throw new ArgumentException("Backup inválido ou incompatível (limite de 2.000 ambientes).");
            if (catalog.Environments.Any(item => item is null || string.IsNullOrEmpty(item.Id))) throw new ArgumentException("Backup contém ambiente sem identificador.");
            var entries = catalog.Environments.Select(item => item with { Id = Rules.EnvironmentId(item.Id) }).ToList();
            if (entries.Select(item => item.Id).Distinct().Count() != entries.Count) throw new ArgumentException("Backup contém identificadores duplicados (maiúsculas/minúsculas são equivalentes).");
            if (Environments().Select(item => item.Id).Concat(entries.Select(item => item.Id)).Distinct().Count() > Rules.MaxEnvironments)
                throw new ArgumentException($"A mesclagem excederia {Rules.MaxEnvironments} ambientes. Nenhuma alteração foi feita.");
            foreach (var entry in catalog.Environments) ValidateEnvironment(entry);
            Rules.Browser(catalog.Settings.Browser);
            using var transaction = connection.BeginTransaction();
            foreach (var entry in entries) Upsert("environments", entry.Id, entry);
            Upsert("settings", "main", catalog.Settings);
            transaction.Commit();
            Log("Importação", $"{catalog.Environments.Count} ambientes mesclados. Agentes não alterados.");
        }
    }

    public void Dispose() => connection.Dispose();
}
