using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WebAgentManager;

var portText = Environment.GetEnvironmentVariable("WAM_PORT") ?? "17643";
if (!int.TryParse(portText, out var port) || port is < 1024 or > 65535) throw new ArgumentException("WAM_PORT deve estar entre 1024 e 65535.");
var origin = $"http://127.0.0.1:{port}";
var packagedWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var options = new WebApplicationOptions { Args = args, WebRootPath = Directory.Exists(packagedWebRoot) ? packagedWebRoot : null };
var builder = WebApplication.CreateBuilder(options);
builder.WebHost.ConfigureKestrel(server =>
{
    server.Listen(IPAddress.Loopback, port);
    server.Limits.MaxRequestBodySize = 512L * 1024 * 1024;
    server.AddServerHeader = false;
});
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.RespectNullableAnnotations = true);
builder.Services.AddSingleton(_ => new Store(Environment.GetEnvironmentVariable("WAM_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProtheusWebAgentManager")));
builder.Services.AddSingleton<IPlatform, WindowsPlatform>();
builder.Services.AddSingleton<AgentService>();
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.CacheControl = "no-store";
    var request = context.Request;
    var permitted = string.Equals(request.Host.Value, $"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
        && (!request.Headers.TryGetValue("Origin", out var requestOrigin) || requestOrigin.ToString() == origin)
        && request.Headers["Sec-Fetch-Site"].ToString() is not ("cross-site" or "same-site")
        && (context.Connection.RemoteIpAddress is null || IPAddress.IsLoopback(context.Connection.RemoteIpAddress));
    if (request.Path.StartsWithSegments("/api") && request.Path != "/api/bootstrap")
    {
        var supplied = Encoding.UTF8.GetBytes(request.Headers["X-WAM-Token"].ToString());
        permitted &= CryptographicOperations.FixedTimeEquals(supplied, Encoding.UTF8.GetBytes(token));
    }
    if (!permitted)
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsJsonAsync(new { error = "Acesso recusado. Abra o painel pelo endereço local e atualize a página." });
        return;
    }
    try { await next(); }
    catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or JsonException or BadHttpRequestException or System.ComponentModel.Win32Exception)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = error is BadHttpRequestException bad ? bad.StatusCode : 400;
        await context.Response.WriteAsJsonAsync(new { error = error is JsonException ? "Dados inválidos. Confira o arquivo ou formulário." : error.Message });
    }
    catch (Exception error)
    {
        app.Logger.LogError(error, "Falha na operação {Path}", request.Path);
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "Não foi possível concluir. Consulte o diagnóstico e tente novamente." });
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/bootstrap", () => new { token, version = typeof(Program).Assembly.GetName().Version!.ToString(3), product = "MultAgentTOTVS", name = "MultAgent TOTVS", localUrl = origin });
app.MapGet("/api/state", (Store store, AgentService agents, IPlatform platform) => new
{
    environments = store.Environments(), agents = store.Agents(), sessions = agents.Sessions(), history = store.History(),
    preparations = agents.Preparations(), settings = store.Settings(), browsers = platform.Browsers(),
    diagnostics = new { dataDirectory = store.Root, defaultAgentDirectory = platform.DefaultAgentDirectory, anyAgentRunning = platform.HasAnyAgent(), user = Environment.UserName, simultaneousValidation = "Pendente de teste com seus ambientes e versões" }
});
app.MapPost("/api/environments", (EnvironmentEntry entry, Store store) => store.SaveEnvironment(entry));
app.MapDelete("/api/environments/{id}", (string id, Store store) => { store.DeleteEnvironment(id); return Results.Ok(new { message = "Cadastro excluído. Arquivos preservados." }); });
app.MapPost("/api/environments/{id}/launch", async (string id, AgentService agents) => await agents.Launch(id));
app.MapPut("/api/settings", (ManagerSettings settings, Store store) => { store.SaveSettings(settings); return Results.Ok(settings); });
app.MapGet("/api/export", (Store store) => Results.File(JsonSerializer.SerializeToUtf8Bytes(store.Export(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), "application/json", $"ambientes-{DateTime.Now:yyyyMMdd-HHmmss}.json"));
app.MapPost("/api/import", (Catalog catalog, Store store) => { store.Import(catalog); return Results.Ok(new { message = "Cadastros mesclados. Arquivos de agentes não foram alterados." }); });
app.MapPost("/api/agents/folder", async (FolderRequest request, AgentService agents) => await agents.ImportFolder(request.Name, request.Path));
app.MapPost("/api/agents/{id}/refresh", async (string id, AgentService agents) => await agents.RefreshAgent(id));
app.MapPost("/api/preparations/upload", async (HttpRequest request, Store store, AgentService agents) =>
{
    var name = request.Query["name"].ToString();
    Rules.Text(name, "Nome do WebAgent", 120);
    var extension = Path.GetExtension(request.Query["fileName"].ToString()).ToLowerInvariant();
    if (extension is not (".exe" or ".msi")) throw new ArgumentException("Envie um instalador .exe ou .msi.");
    if (request.ContentType != "application/octet-stream") throw new ArgumentException("Formato de envio inválido.");
    if (request.ContentLength is null or < 1 or > 536870912) throw new ArgumentException("O instalador deve ter entre 1 byte e 512 MB.");
    var directory = Path.Combine(store.Root, "installers");
    SafeFiles.NoLinks(directory);
    Directory.CreateDirectory(directory);
    var destination = Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
    try
    {
        await using (var file = new FileStream(destination, FileMode.CreateNew)) await request.Body.CopyToAsync(file, request.HttpContext.RequestAborted);
        return Results.Ok(await agents.RegisterInstaller(name, destination));
    }
    catch { if (File.Exists(destination)) File.Delete(destination); throw; }
});
app.MapPost("/api/preparations/{id}/install", async (string id, ConsentRequest request, AgentService agents) => await agents.Install(id, request.Consent));
app.MapPost("/api/preparations/{id}/capture", async (string id, AgentService agents) => await agents.CaptureInstalled(id));
app.MapPost("/api/preparations/{id}/cancel", async (string id, AgentService agents) => { await agents.CancelPreparation(id); return Results.Ok(new { message = "Preparação cancelada. Backups preservados." }); });

if (args.Contains("--open") && OperatingSystem.IsWindows())
    app.Lifetime.ApplicationStarted.Register(() => Process.Start(new ProcessStartInfo(origin) { UseShellExecute = true }));

app.Run();

public record FolderRequest(string Name, string Path);
public record ConsentRequest(bool Consent);
public partial class Program { }
