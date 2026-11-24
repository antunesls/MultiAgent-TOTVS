using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebAgentManager;
using Xunit;

namespace WebAgentManager.Tests;

public sealed class ApiTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "wam-api-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;
    private readonly Store storeInstance;
    public ApiTests()
    {
        storeInstance = new Store(root);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<Store>();
            services.AddSingleton(storeInstance);
            services.RemoveAll<IPlatform>();
            services.AddSingleton<IPlatform>(new FakePlatform());
        }));
        client = factory.CreateClient(new() { BaseAddress = new Uri("http://127.0.0.1:17643") });
    }

    private async Task Authorize()
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/bootstrap");
        client.DefaultRequestHeaders.Add("X-WAM-Token", response.GetProperty("token").GetString());
    }

    [Fact]
    public async Task ApiRecusaTokenAusente() => Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/state")).StatusCode);

    [Fact]
    public async Task BootstrapRecusaHostExterno()
    {
        client.DefaultRequestHeaders.Host = "attacker.example:17643";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/bootstrap")).StatusCode);
    }

    [Fact]
    public async Task RecusaOriginExternoMesmoComToken()
    {
        await Authorize();
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/state")).StatusCode);
    }

    [Fact]
    public async Task BootstrapRecusaRequisicaoCrossSite()
    {
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/bootstrap")).StatusCode);
    }

    [Fact]
    public async Task EstadoAutenticadoECrudFuncionam()
    {
        await Authorize();
        var state = await client.GetAsync("/api/state");
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
        var store = factory.Services.GetRequiredService<Store>();
        store.AddAgent(new("teste", "Teste", "1.1.1", "x64", root, "hash"));
        var response = await client.PostAsJsonAsync("/api/environments", new EnvironmentEntry { Client = "Cliente", Name = "HML", Url = "http://cliente:8080/webapp/", AgentId = "teste" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = (await response.Content.ReadFromJsonAsync<EnvironmentEntry>())!;
        Assert.NotEqual("", entry.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/environments/" + entry.Id)).StatusCode);
        Assert.Empty(store.Environments());
    }

    [Fact]
    public async Task RecusaAbrirAmbienteInexistente()
    {
        await Authorize();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/environments/ausente/launch", new { })).StatusCode);
    }

    [Fact]
    public async Task RecusaCamposNullSemErroInterno()
    {
        await Authorize();
        var body = new StringContent("{\"client\":null,\"name\":\"Teste\",\"url\":\"http://cliente/\",\"agentId\":\"teste\"}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/environments", body)).StatusCode);
    }

    [Fact]
    public async Task PublicaPainelComCspESemRecursosRemotos()
    {
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("default-src 'self'", string.Join("", response.Headers.GetValues("Content-Security-Policy")));
        Assert.Contains("Ambientes", await response.Content.ReadAsStringAsync());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
        storeInstance.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
