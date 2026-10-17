using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LearnSphere.Api.Configuration;
using LearnSphere.Api.Domain;
using LearnSphere.Api.Generation;
using LearnSphere.Api.Jobs;
using LearnSphere.Api.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Tests;

public sealed class FailTerminalSave : SaveChangesInterceptor
{
    public int Remaining { get; set; } = 1;
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (Remaining > 0 && data.Context!.ChangeTracker.Entries<LearnSphere.Api.Domain.Generation>().Any(e => e.State == EntityState.Modified && e.Entity.Status is GenerationStatus.Succeeded or GenerationStatus.Failed))
        { Remaining--; throw new DbUpdateException("Simulated storage outage"); }
        return ValueTask.FromResult(result);
    }
}
public sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "Tests";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
public class RecoveryTests
{
    public static ResultJournal Journal(string? directory = null) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Recovery:Directory"] = directory ?? Path.Combine(Path.GetTempPath(), "ls-recovery-" + Guid.NewGuid().ToString("N")) }).Build(), new TestEnvironment());
    [Fact]
    public async Task ReadOnlyDatabaseCannotRecordFailureButCheckpointResumesWhenWritesReturn()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        var job = new LearnSphere.Api.Domain.Generation { Status = GenerationStatus.Processing, Transcript = GenerationTests.Transcript }; db.Add(job); db.GenerationIntents.Add(new() { GenerationId = job.Id, Provider = "Mock", Model = "" }); await db.SaveChangesAsync();
        var fake = new ReadOnlyClient(connection); var journal = Journal(); var processor = new GenerationProcessor(db, GenerationTests.Service(fake), NullLogger<GenerationProcessor>.Instance, Options.Create(new LlmOptions()), journal);
        await processor.ProcessAsync(job.Id, default);
        Assert.True(journal.IsFailed(job.Id)); Assert.NotNull(journal.Read(job.Id));
        Assert.Equal(GenerationStatus.Processing, (await db.Generations.AsNoTracking().SingleAsync()).Status);
        await using (var command = connection.CreateCommand()) { command.CommandText = "PRAGMA query_only=OFF"; await command.ExecuteNonQueryAsync(); }
        db.ChangeTracker.Clear(); Assert.True(await processor.ResumePersistenceAsync(job.Id, default)); Assert.Equal(1, fake.Calls);
        var result = await db.Generations.AsNoTracking().SingleAsync(); Assert.Equal(GenerationStatus.Succeeded, result.Status); Assert.Null(result.Transcript);
    }
    [Fact]
    public async Task UnwritableCheckpointRetainsResultInMemoryForSaveRetry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        var job = new LearnSphere.Api.Domain.Generation { Status = GenerationStatus.Processing, Transcript = GenerationTests.Transcript }; db.Add(job); db.GenerationIntents.Add(new() { GenerationId = job.Id, Provider = "Mock", Model = "" }); await db.SaveChangesAsync();
        string directory = Path.Combine(Path.GetTempPath(), "ls-recovery-" + Guid.NewGuid().ToString("N")); var journal = Journal(directory);
        Directory.Delete(directory); await File.WriteAllTextAsync(directory, "Simulate unavailable directory");
        var fake = new ScriptedLlmClient(GenerationTests.Response()); var processor = new GenerationProcessor(db, GenerationTests.Service(fake), NullLogger<GenerationProcessor>.Instance, Options.Create(new LlmOptions()), journal);
        await processor.ProcessAsync(job.Id, default); Assert.Equal("result_persistence_failed", (await db.Generations.AsNoTracking().SingleAsync()).ErrorCode);
        File.Delete(directory); Directory.CreateDirectory(directory); db.ChangeTracker.Clear();
        Assert.True(await processor.ResumePersistenceAsync(job.Id, default)); Assert.Equal(1, fake.Calls);
    }
    [Fact]
    public async Task FailedSaveResumesOnlyPersistenceEvenAfterJournalReopen()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var interceptor = new FailTerminalSave();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(interceptor).Options);
        await db.Database.EnsureCreatedAsync();
        var job = new LearnSphere.Api.Domain.Generation { Status = GenerationStatus.Processing, Transcript = GenerationTests.Transcript };
        db.Add(job); db.GenerationIntents.Add(new() { GenerationId = job.Id, Provider = "Mock", Model = "" }); await db.SaveChangesAsync();
        string directory = Path.Combine(Path.GetTempPath(), "ls-recovery-" + Guid.NewGuid().ToString("N"));
        var journal = Journal(directory); var fake = new ScriptedLlmClient(GenerationTests.Response());
        var processor = new GenerationProcessor(db, GenerationTests.Service(fake), NullLogger<GenerationProcessor>.Instance, Options.Create(new LlmOptions()), journal);
        await processor.ProcessAsync(job.Id, default);
        var failed = await db.Generations.AsNoTracking().SingleAsync();
        Assert.Equal(GenerationStatus.Failed, failed.Status); Assert.Equal("result_persistence_failed", failed.ErrorCode); Assert.Null(failed.Transcript); Assert.NotNull(journal.Read(job.Id));
        db.ChangeTracker.Clear();
        var reopened = Journal(directory);
        processor = new(db, GenerationTests.Service(fake), NullLogger<GenerationProcessor>.Instance, Options.Create(new LlmOptions()), reopened);
        Assert.True(await processor.ResumePersistenceAsync(job.Id, default));
        var saved = await db.Generations.AsNoTracking().SingleAsync();
        Assert.Equal(GenerationStatus.Succeeded, saved.Status); Assert.NotNull(saved.Content); Assert.Null(saved.Transcript);
        Assert.Null(saved.ErrorCode);
        Assert.Equal(1, fake.Calls); Assert.Null(reopened.Read(job.Id));
    }
    [Theory]
    [InlineData("Mock", "Gemini")]
    [InlineData("Gemini", "Mock")]
    public async Task ProviderChangeNeverCallsTheNewProvider(string submitted, string current)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        var job = new LearnSphere.Api.Domain.Generation { Status = GenerationStatus.Processing, Transcript = GenerationTests.Transcript, GenerationMode = submitted == "Mock" ? "mock" : "live" };
        db.Add(job); db.GenerationIntents.Add(new() { GenerationId = job.Id, Provider = submitted, Model = "model-a" }); await db.SaveChangesAsync();
        var fake = new ScriptedLlmClient();
        await new GenerationProcessor(db, GenerationTests.Service(fake), NullLogger<GenerationProcessor>.Instance, Options.Create(new LlmOptions { Provider = current, Model = "model-a" }), Journal()).ProcessAsync(job.Id, default);
        Assert.Equal("provider_configuration_changed", job.ErrorCode); Assert.Equal(0, fake.Calls); Assert.Null(job.Transcript);
    }
    [Fact]
    public async Task CapabilitiesAndValidationUseChangedServerLimits()
    {
        using var factory = new ApiFactory { Overrides = new() { ["Generation:MinTranscriptChars"] = "50", ["Generation:MaxTranscriptChars"] = "1000", ["Generation:MaxTitleChars"] = "30" } };
        using var http = factory.CreateClient(); var caps = await http.GetFromJsonAsync<JsonElement>("/api/v1/capabilities");
        Assert.Equal(50, caps.GetProperty("minTranscriptChars").GetInt32()); Assert.Equal(1000, caps.GetProperty("maxTranscriptChars").GetInt32()); Assert.Equal(30, caps.GetProperty("maxTitleChars").GetInt32());
        Assert.Equal(HttpStatusCode.Accepted, (await http.PostAsJsonAsync("/api/v1/generations", new { transcript = new string('x', 75) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/v1/generations", new { transcript = new string('x', 1001) })).StatusCode);
    }
    [Fact]
    public async Task ApiRetriesSaveOnSameIdWithoutCallingProviderAgain()
    {
        var fake = new ScriptedLlmClient(GenerationTests.Response());
        using var factory = new ApiFactory { Llm = fake, SaveInterceptor = new FailTerminalSave() }; using var http = factory.CreateClient();
        var created = await http.PostAsJsonAsync("/api/v1/generations", new { transcript = GenerationTests.Transcript }); var path = created.Headers.Location!.ToString();
        JsonElement result = default;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        do { await Task.Delay(20, timeout.Token); result = await http.GetFromJsonAsync<JsonElement>(path, timeout.Token); } while (result.GetProperty("status").GetString() is "Queued" or "Processing");
        Assert.Equal("PersistResult", result.GetProperty("error").GetProperty("resumeStage").GetString());
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(path + "/retry", new { })).StatusCode);
        result = await http.GetFromJsonAsync<JsonElement>(path); Assert.Equal("Succeeded", result.GetProperty("status").GetString()); Assert.Equal(JsonValueKind.Null, result.GetProperty("error").ValueKind); Assert.Equal(1, fake.Calls);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(path + "/retry", new { })).StatusCode);
    }
    private sealed class ReadOnlyClient(SqliteConnection connection) : ILlmClient
    {
        public int Calls { get; private set; }
        public Task<int?> CountTokensAsync(LlmRequest request, CancellationToken ct) => Task.FromResult<int?>(100);
        public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct)
        { Calls++; await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA query_only=ON"; await command.ExecuteNonQueryAsync(ct); return GenerationTests.Response(); }
    }
}
