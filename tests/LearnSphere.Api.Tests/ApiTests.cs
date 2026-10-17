using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LearnSphere.Api.Domain;
using LearnSphere.Api.Generation;
using LearnSphere.Api.Jobs;
using LearnSphere.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string database = $"Data Source=ls-{Guid.NewGuid()};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection keeper;
    public ILlmClient? Llm { get; init; }
    public bool InvalidLive { get; init; }
    public int MaxPending { get; init; } = 20;
    public Dictionary<string, string?> Overrides { get; init; } = [];
    public Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor? SaveInterceptor { get; init; }
    private readonly string webRoot = Path.Combine(Path.GetTempPath(), "learnsphere-test-web-" + Guid.NewGuid().ToString("N"));
    public ApiFactory() { keeper = new(database); keeper.Open(); }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<html><body>LearnSphere test SPA</body></html>");
        builder.UseWebRoot(webRoot);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = database,
            ["Llm:Provider"] = InvalidLive ? "Gemini" : "Mock",
            ["Llm:Model"] = "",
            ["Llm:ApiKey"] = "",
            ["Generation:MaxPendingJobs"] = MaxPending.ToString()
        }).AddInMemoryCollection(Overrides));
        builder.ConfigureServices(services => { if (Llm is not null) { services.RemoveAll<ILlmClient>(); services.AddSingleton(Llm); } if (SaveInterceptor is not null) services.AddDbContext<AppDbContext>(o => o.AddInterceptors(SaveInterceptor)); });
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) keeper.Dispose(); }
}
public class ApiTests
{
    private const string Base = "/api/v1/generations";
    private static async Task<JsonElement> Terminal(HttpClient client, string location)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var result = await client.GetFromJsonAsync<JsonElement>(location, deadline.Token);
            if (result.GetProperty("status").GetString() is "Succeeded" or "Failed") return result;
            await Task.Delay(30, deadline.Token);
        }
    }
    [Fact]
    public async Task EndToEndListAndDeleteWithoutTranscriptDisclosure()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(Base, new { title = "Course", transcript = GenerationTests.Transcript });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); Assert.NotNull(response.Headers.Location);
        var job = await Terminal(client, response.Headers.Location!.ToString()); Assert.Equal("Succeeded", job.GetProperty("status").GetString());
        Assert.Equal("mock", job.GetProperty("generationMode").GetString()); Assert.Equal(5, job.GetProperty("questions").GetArrayLength());
        Assert.EndsWith("Z", job.GetProperty("createdAt").GetString());
        Assert.All(job.GetProperty("questions").EnumerateArray(), q => { Assert.Equal(4, q.GetProperty("options").GetArrayLength()); Assert.True(q.GetProperty("excerptVerified").GetBoolean()); });
        using (var scope = factory.Services.CreateScope()) Assert.Null((await scope.ServiceProvider.GetRequiredService<AppDbContext>().Generations.SingleAsync()).Transcript);
        var list = await client.GetStringAsync(Base); Assert.DoesNotContain("transcript", list); Assert.DoesNotContain("questions", list); Assert.Contains("Course", list);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(response.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(response.Headers.Location)).StatusCode);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"transcript\":\"   \"}")]
    [InlineData("{")]
    [InlineData("null")]
    public async Task InvalidJsonAndMissingTranscript(string body)
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        var response = await client.PostAsync(Base, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("traceId", await response.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData(499)]
    [InlineData(200001)]
    public async Task LengthLimits(int length)
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        var result = await client.PostAsJsonAsync(Base, new { transcript = new string('x', length) }); Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); Assert.Contains("errors", await result.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task TitleContentTypeBodyLimitAndInvalidListLimit()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Base, new { title = new string('x', 201), transcript = GenerationTests.Transcript })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Base, new StringContent("plain"))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync(Base, new StringContent(new string('x', 1_048_577), Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Base + "?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Base + "?limit=nope")).StatusCode);
    }
    [Fact]
    public async Task HostingAndUnknownRoutes()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        foreach (var path in new[] { "/api/v1/nope", Base + "/" + Guid.NewGuid() }) { var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType); }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Contains("LearnSphere test SPA", await client.GetStringAsync("/generations/example-deep-link"));
        Assert.Contains("CreateGenerationRequest", await client.GetStringAsync("/openapi/v1.json"));
    }
    [Fact]
    public void LiveWithoutCredentialsFailsAtStartup()
    {
        using var factory = new ApiFactory { InvalidLive = true }; Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }
    [Fact]
    public async Task AdmissionIsBoundedAndProcessingCannotBeDeleted()
    {
        var blocking = new BlockingLlmClient(); using var factory = new ApiFactory { Llm = blocking, MaxPending = 1 }; using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync(Base, new { transcript = GenerationTests.Transcript });
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(Base, new { transcript = GenerationTests.Transcript })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(created.Headers.Location)).StatusCode);
        blocking.Release.TrySetResult(); await Terminal(client, created.Headers.Location!.ToString());
    }
    [Fact]
    public async Task StartupSweepFailsInterruptedAndProcessesQueued()
    {
        // Exercise real worker startup on a seeded SQLite database without racing host startup.
        var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var services = new ServiceCollection(); services.AddLogging(); services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<ILlmClient, LearnSphere.Api.Providers.MockLlmClient>(); services.AddScoped<TrainingContentService>(); services.AddScoped<GenerationProcessor>();
        services.AddSingleton(Options.Create(new LearnSphere.Api.Configuration.LlmOptions())); services.AddSingleton(Options.Create(new LearnSphere.Api.Configuration.GenerationOptions()));
        services.AddSingleton(RecoveryTests.Journal());
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.EnsureCreatedAsync();
        var interrupted = new LearnSphere.Api.Domain.Generation { Status = GenerationStatus.Processing, Transcript = GenerationTests.Transcript };
        var queued = new LearnSphere.Api.Domain.Generation { Transcript = GenerationTests.Transcript };
        db.AddRange(interrupted, queued); db.GenerationIntents.Add(new() { GenerationId = queued.Id, Provider = "Mock", Model = "" }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        using var signal = new GenerationSignal(); using var worker = new GenerationWorker(provider.GetRequiredService<IServiceScopeFactory>(), signal, Options.Create(new LearnSphere.Api.Configuration.GenerationOptions()), NullLogger<GenerationWorker>.Instance);
        await worker.StartAsync(default);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while ((await db.Generations.AsNoTracking().SingleAsync(x => x.Id == queued.Id, deadline.Token)).Status is GenerationStatus.Queued or GenerationStatus.Processing) await Task.Delay(20, deadline.Token);
        await worker.StopAsync(default);
        var stale = await db.Generations.AsNoTracking().SingleAsync(x => x.Id == interrupted.Id); Assert.Equal("interrupted", stale.ErrorCode); Assert.Null(stale.Transcript);
        var done = await db.Generations.AsNoTracking().SingleAsync(x => x.Id == queued.Id); Assert.Equal(GenerationStatus.Succeeded, done.Status); Assert.Null(done.Transcript);
        await connection.DisposeAsync();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessorClearsTranscriptOnFailures(bool unexpected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        var job = new LearnSphere.Api.Domain.Generation { Status = GenerationStatus.Processing, Transcript = GenerationTests.Transcript }; db.Add(job); db.GenerationIntents.Add(new() { GenerationId = job.Id, Provider = "Mock", Model = "" }); await db.SaveChangesAsync();
        var fake = new ScriptedLlmClient(unexpected ? new InvalidOperationException("private") : new LlmException(LlmErrorKind.Unavailable));
        await new GenerationProcessor(db, GenerationTests.Service(fake), NullLogger<GenerationProcessor>.Instance, Options.Create(new LearnSphere.Api.Configuration.LlmOptions()), RecoveryTests.Journal()).ProcessAsync(job.Id, default);
        Assert.Null(job.Transcript); Assert.Equal(GenerationStatus.Failed, job.Status); Assert.Equal(unexpected ? "internal_error" : "provider_unavailable", job.ErrorCode);
    }
}
public sealed class BlockingLlmClient : ILlmClient
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<int?> CountTokensAsync(LlmRequest r, CancellationToken ct) => Task.FromResult<int?>(100);
    public async Task<LlmResponse> GenerateAsync(LlmRequest r, CancellationToken ct) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return GenerationTests.Response(); }
}
