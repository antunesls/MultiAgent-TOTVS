using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebAgentManager;

public record BrowserEntry(string Name, string Path);
public interface IRunningProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    int? ExitCode { get; }
}
public interface IPlatform
{
    string DefaultAgentDirectory { get; }
    Task<FileInspection> Inspect(string path);
    bool HasAnyAgent();
    IRunningProcess Start(ProcessStartInfo info);
    IReadOnlyList<BrowserEntry> Browsers();
}

public sealed class WindowsPlatform : IPlatform
{
    public string DefaultAgentDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "web-agent");

    public async Task<FileInspection> Inspect(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new ArgumentException("Este aplicativo requer Windows.");
        await using var stream = File.OpenRead(path);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        var version = "Não identificada";
        var architecture = "Instalador";
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var metadata = FileVersionInfo.GetVersionInfo(path);
            version = metadata.ProductVersion ?? metadata.FileVersion ?? version;
            stream.Position = 0;
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (stream.Length >= 64 && reader.ReadUInt16() == 0x5A4D)
            {
                stream.Position = 0x3c;
                var offset = reader.ReadInt32();
                if (offset >= 0 && offset <= stream.Length - 6)
                {
                    stream.Position = offset;
                    if (reader.ReadUInt32() == 0x4550)
                        architecture = reader.ReadUInt16() switch { 0x8664 => "x64", 0x014c => "x86", 0xaa64 => "ARM64", _ => "Desconhecida" };
                }
            }
        }
        // Caminho é argumento literal escapado, nunca um comando vindo do navegador.
        var literal = path.Replace("'", "''");
        var script = $"$ErrorActionPreference='Stop'; $s=Get-AuthenticodeSignature -LiteralPath '{literal}'; @{{status=[string]$s.Status; signer=[string]$s.SignerCertificate.Subject}} | ConvertTo-Json -Compress";
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new ArgumentException("Não foi possível verificar a assinatura digital.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill();
            throw new ArgumentException("A verificação de assinatura excedeu 60 segundos. Confira acesso à rede e certificados do Windows.");
        }
        var output = await outputTask;
        await errorTask;
        if (process.ExitCode != 0) throw new ArgumentException("O Windows não conseguiu verificar a assinatura digital do arquivo.");
        using var document = JsonDocument.Parse(output);
        var status = document.RootElement.GetProperty("status").GetString() ?? "Unknown";
        var signer = document.RootElement.GetProperty("signer").GetString() ?? "";
        var trusted = status == "Valid" && Rules.TotvsSigner(signer);
        return new(version.Trim(), architecture, sha, trusted, signer, status);
    }

    public bool HasAnyAgent()
    {
        var processes = Process.GetProcessesByName("web-agent");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public IRunningProcess Start(ProcessStartInfo info) => new RunningProcess(Process.Start(info) ?? throw new ArgumentException("O Windows não iniciou o programa."));

    public IReadOnlyList<BrowserEntry> Browsers()
    {
        var candidates = new[] { ("Microsoft Edge", @"Microsoft\Edge\Application\msedge.exe"), ("Google Chrome", @"Google\Chrome\Application\chrome.exe"), ("Mozilla Firefox", @"Mozilla Firefox\firefox.exe"), ("Brave", @"BraveSoftware\Brave-Browser\Application\brave.exe") };
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
        return candidates.SelectMany(candidate => roots.Select(root => new BrowserEntry(candidate.Item1, Path.Combine(root, candidate.Item2)))).Where(browser => File.Exists(browser.Path)).DistinctBy(browser => browser.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed class RunningProcess(Process initialProcess) : IRunningProcess
    {
        private readonly object gate = new();
        private Process? process = initialProcess;
        private bool exited;
        private int? exitCode;
        public int Id { get; } = initialProcess.Id;
        private void Refresh()
        {
            if (process is null) return;
            exited = process.HasExited;
            if (exited)
            {
                exitCode = process.ExitCode;
                process.Dispose();
                process = null;
            }
        }
        public bool HasExited { get { lock (gate) { Refresh(); return exited; } } }
        public int? ExitCode { get { lock (gate) { Refresh(); return exitCode; } } }
        public void Dispose()
        {
            lock (gate)
            {
                Refresh();
                process?.Dispose();
                process = null;
            }
        }
    }
}
