using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WebAgentManager;

public record EnvironmentEntry
{
    public string Id { get; init; } = "";
    public string Client { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "Homologação";
    public string Url { get; init; } = "";
    public string AgentId { get; init; } = "";
    public string Browser { get; init; } = "";
    public string Notes { get; init; } = "";
    public bool Favorite { get; init; }
}

public record AgentEntry(string Id, string Name, string Version, string Architecture, string Directory, string Sha256);
public record HistoryEntry(string Time, string Action, string Detail);
public record ManagerSettings(string Browser = "");
public record Catalog(int Schema, List<EnvironmentEntry> Environments, ManagerSettings Settings);
public record SessionEntry(string Id, string EnvironmentId, string Name, int Pid, string StartedAt, bool Running);
public record FileInspection(string Version, string Architecture, string Sha256, bool Trusted, string Signer, string Status);
public record PreparationEntry(string Id, string Name, string FileName, FileInspection Inspection, string Stage, string? Backup = null, int? InstallerPid = null);

public static class Rules
{
    public const int MaxEnvironments = 2000;

    public static string EnvironmentId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out var guid)) throw new ArgumentException("Identificador do ambiente inválido.");
        return guid.ToString("N");
    }

    public static bool TotvsSigner(string subject)
    {
        var match = Regex.Match(subject, "(?:^|,\\s*)O=(\"[^\"]+\"|[^,]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        var organization = Regex.Replace(match.Groups[1].Value.ToUpperInvariant(), "[^A-Z0-9]", "");
        return organization is "TOTVS" or "TOTVSSA" or "MICROSIGASOFTWARESA";
    }

    public static void LaunchSupported(string version, string browser)
    {
        var match = Regex.Match(version, @"\d+\.\d+\.\d+(?:\.\d+)?", RegexOptions.CultureInvariant);
        if (!match.Success || !Version.TryParse(match.Value, out var parsed))
            throw new ArgumentException("Não foi possível identificar a versão do WebAgent. Importe o pacote oficial completo, incluindo package.json, e atualize a identificação do agente.");
        if (parsed < new Version(1, 0, 16)) throw new ArgumentException("Este gerenciador exige WebAgent 1.0.16 ou superior para abertura isolada com launch.");
        if (browser == "" && parsed < new Version(1, 0, 24)) throw new ArgumentException("WebAgent anterior à versão 1.0.24: selecione explicitamente um navegador no ambiente ou nas configurações.");
    }

    public static string WebUrl(string value)
    {
        if (value.Any(char.IsControl) || value.Length > 4096 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || uri.Port is < 1 or > 65535)
            throw new ArgumentException("Informe uma URL HTTP/HTTPS válida, sem usuário ou senha.");
        return value;
    }

    public static void Environment(EnvironmentEntry entry)
    {
        Text(entry.Client, "Cliente", 120);
        Text(entry.Name, "Ambiente", 120);
        Text(entry.AgentId, "WebAgent", 80);
        if (entry.Notes.Length > 4000) throw new ArgumentException("Observações: limite de 4.000 caracteres.");
        if (!new[] { "Produção", "Homologação", "Desenvolvimento", "Outro" }.Contains(entry.Kind))
            throw new ArgumentException("Tipo de ambiente inválido.");
        WebUrl(entry.Url);
        Browser(entry.Browser);
    }

    public static void Text(string text, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > max || text.Any(char.IsControl))
            throw new ArgumentException($"{field}: informe de 1 a {max} caracteres, sem caracteres de controle.");
    }

    public static void Browser(string value)
    {
        if (value == "") return;
        if (value.Length > 1024) throw new ArgumentException("Navegador: limite de 1.024 caracteres no caminho.");
        if (!Path.IsPathFullyQualified(value) || !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || value.Any(char.IsControl))
            throw new ArgumentException("Navegador: informe o caminho completo do executável .exe.");
        if (!new[] { "msedge.exe", "chrome.exe", "firefox.exe", "brave.exe" }.Contains(Path.GetFileName(value).ToLowerInvariant()))
            throw new ArgumentException("Escolha Edge, Chrome, Firefox ou Brave. Compatibilidade depende da versão do WebApp.");
    }

    public static ProcessStartInfo LaunchInfo(string executable, string url, string browser)
    {
        WebUrl(url);
        Browser(browser);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add("launch");
        start.ArgumentList.Add(url);
        if (browser != "") start.ArgumentList.Add("--browser=" + browser);
        return start;
    }
}
