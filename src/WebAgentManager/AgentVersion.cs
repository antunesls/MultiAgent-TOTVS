using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebAgentManager;

public static class AgentVersion
{
    private static bool Valid(string? value) => value is not null && value.Length <= 100 &&
        Regex.IsMatch(value, @"^\d+\.\d+\.\d+(?:\.\d+)?(?:-[a-zA-Z0-9.-]+)?(?:\+[a-zA-Z0-9.-]+)?$", RegexOptions.CultureInvariant);

    public static string Resolve(string executable, string metadata)
    {
        var manifest = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!, "package.json");
        SafeFiles.NoLinks(manifest);
        if (File.Exists(manifest))
        {
            if (new FileInfo(manifest).Length > 65536) throw new ArgumentException("O package.json do WebAgent excede o tamanho permitido (64 KB).");
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == "web-agent" &&
                    root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String && Valid(version.GetString()))
                    return version.GetString()!;
            }
            catch (JsonException) { /* Erro orientado ao usuário abaixo, sem presumir uma versão. */ }
            throw new ArgumentException("O package.json não identifica uma versão válida do web-agent. Confira a pasta instalada e importe uma cópia completa do pacote oficial.");
        }
        if (Valid(metadata.Trim())) return metadata.Trim();
        throw new ArgumentException("Versão real não identificada: o executável possui metadados genéricos e a pasta não contém package.json. Importe a pasta completa do WebAgent; não é necessário reinstalar as cópias existentes.");
    }
}
