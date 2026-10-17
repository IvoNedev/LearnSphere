using LearnSphere.Api.Configuration;
using LearnSphere.Api.Domain;
using LearnSphere.Api.Generation;
using LearnSphere.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Jobs;

// Admission and delete are serialized in this single-host POC; DB claims remain conditional.
public sealed class GenerationSignal : IDisposable
{
    private readonly SemaphoreSlim wake = new(0, 1);
    public SemaphoreSlim Admission { get; } = new(1, 1);
    public void Pulse() { if (wake.CurrentCount == 0) { try { wake.Release(); } catch (SemaphoreFullException) { } } }
    public Task<bool> WaitAsync(CancellationToken ct) => wake.WaitAsync(TimeSpan.FromSeconds(5), ct);
    public void Dispose() { wake.Dispose(); Admission.Dispose(); }
}
public sealed class GenerationProcessor(AppDbContext db, TrainingContentService service, ILogger<GenerationProcessor> logger, IOptions<LlmOptions> llm, ResultJournal journal)
{
    public async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        var job = await db.Generations.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (job is null || job.Status != GenerationStatus.Processing) return;
        GenerationResult result;
        try
        {
            var intent = await db.GenerationIntents.FindAsync([id], ct);
            if (intent is null || intent.Provider != llm.Value.Provider || intent.Model != llm.Value.Model)
            {
                result = Failure("provider_configuration_changed");
            }
            else result = await service.GenerateAsync(job.Transcript ?? "", ct, intent.UseChunking);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Generation failed. Id={Id} ExceptionType={ExceptionType}", id, ex.GetType().Name);
            result = Failure("internal_error");
        }
        await PersistAsync(job, result, ct);
        logger.LogInformation("Generation finished. Id={Id} Status={Status} Provider={Provider} Model={Model} PromptVersion={PromptVersion} TranscriptChars={TranscriptChars} TranscriptSha256={TranscriptSha256} DurationMs={DurationMs} InputTokens={InputTokens} OutputTokens={OutputTokens} ThoughtTokens={ThoughtTokens} RepairAttempted={RepairAttempted} ErrorCode={ErrorCode}",
            id, job.Status, job.Metadata?.Provider, job.Metadata?.Model, job.Metadata?.PromptVersion, job.TranscriptChars, job.TranscriptSha256, job.Metadata?.DurationMs, job.Metadata?.InputTokens, job.Metadata?.OutputTokens, job.Metadata?.ThoughtTokens, job.Metadata?.RepairAttempted, job.ErrorCode);
        GenerationResult Failure(string code) => new(null, new(llm.Value.Provider, llm.Value.Model, Prompts.Version, job.TranscriptChars, null, null, null, 0, false), code);
    }
    public async Task<bool> ResumePersistenceAsync(Guid id, CancellationToken ct)
    {
        var result = journal.Read(id);
        var job = await db.Generations.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (result is null || job is null || (job.ErrorCode != "result_persistence_failed" && !journal.IsFailed(id))) return false;
        return await PersistAsync(job, result, ct);
    }
    private async Task<bool> PersistAsync(Domain.Generation job, GenerationResult result, CancellationToken ct)
    {
        try
        {
            await journal.StageAsync(job.Id, result, ct);
            if (result.ErrorCode is { } code) job.Fail(code, result.Metadata);
            else job.Succeed(result.Content!, result.Metadata);
            job.GenerationMode = result.Metadata.Provider == "Mock" ? "mock" : "live";
            await db.SaveChangesAsync(ct);
            journal.Complete(job.Id); return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is DbUpdateException or Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
            journal.MarkFailed(job.Id);
            job.Fail("result_persistence_failed", result.Metadata);
            logger.LogError("Result save failed. Id={Id} ExceptionType={ExceptionType} ResumeStage=PersistResult", job.Id, ex.GetType().Name);
            try
            {
                // Persistence-only failure: never requeue inference or discard the result checkpoint.
                await db.Generations.Where(x => x.Id == job.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GenerationStatus.Failed)
                    .SetProperty(x => x.ErrorCode, "result_persistence_failed").SetProperty(x => x.Transcript, (string?)null).SetProperty(x => x.CompletedAt, DateTime.UtcNow), ct);
            }
            catch (Exception writeError) when (writeError is Microsoft.Data.Sqlite.SqliteException or DbUpdateException)
            { logger.LogError("Database cannot record failure yet. Id={Id} ResumeStage=PersistResult", job.Id); }
            return false;
        }
    }
}
public sealed class GenerationWorker(IServiceScopeFactory scopes, GenerationSignal signal, IOptions<GenerationOptions> options, ILogger<GenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Recovery happens once before this host starts processing. Multi-host deployment is unsupported.
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var journal = scope.ServiceProvider.GetRequiredService<ResultJournal>();
            foreach (var job in await db.Generations.Where(x => x.Status == GenerationStatus.Processing || x.ErrorCode == "result_persistence_failed").ToListAsync(stoppingToken))
            {
                try
                {
                    if (journal.Read(job.Id) is not null) { job.Fail("result_persistence_failed"); journal.MarkFailed(job.Id); }
                    else if (job.Status == GenerationStatus.Processing) job.Fail("interrupted");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                { job.Fail("result_checkpoint_unavailable"); }
            }
            await db.SaveChangesAsync(stoppingToken);
        }
        var workers = Enumerable.Range(0, options.Value.MaxConcurrentJobs).Select(_ => RunAsync(stoppingToken));
        await Task.WhenAll(workers);
    }
    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var next = await db.Generations.AsNoTracking().Where(x => x.Status == GenerationStatus.Queued).OrderBy(x => x.CreatedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
                if (next is null) { await signal.WaitAsync(ct); continue; }
                var claimed = await db.Generations.Where(x => x.Id == next && x.Status == GenerationStatus.Queued)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GenerationStatus.Processing).SetProperty(x => x.StartedAt, DateTime.UtcNow), ct);
                if (claimed == 1) await scope.ServiceProvider.GetRequiredService<GenerationProcessor>().ProcessAsync(next.Value, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError("Worker iteration failed. ExceptionType={ExceptionType}", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
}
