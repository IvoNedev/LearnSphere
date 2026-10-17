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
public sealed class GenerationProcessor(AppDbContext db, TrainingContentService service, ILogger<GenerationProcessor> logger)
{
    public async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        var job = await db.Generations.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (job is null || job.Status != GenerationStatus.Processing) return;
        try
        {
            var result = await service.GenerateAsync(job.Transcript ?? "", ct);
            if (result.ErrorCode is { } error) job.Fail(error, result.Metadata);
            else job.Succeed(result.Content!, result.Metadata);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Generation failed. Id={Id} ExceptionType={ExceptionType}", id, ex.GetType().Name);
            job.Fail("internal_error");
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Generation finished. Id={Id} Status={Status} Provider={Provider} Model={Model} PromptVersion={PromptVersion} TranscriptChars={TranscriptChars} TranscriptSha256={TranscriptSha256} DurationMs={DurationMs} InputTokens={InputTokens} OutputTokens={OutputTokens} ThoughtTokens={ThoughtTokens} RepairAttempted={RepairAttempted} ErrorCode={ErrorCode}",
            id, job.Status, job.Metadata?.Provider, job.Metadata?.Model, job.Metadata?.PromptVersion, job.TranscriptChars, job.TranscriptSha256, job.Metadata?.DurationMs, job.Metadata?.InputTokens, job.Metadata?.OutputTokens, job.Metadata?.ThoughtTokens, job.Metadata?.RepairAttempted, job.ErrorCode);
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
            await db.Generations.Where(x => x.Status == GenerationStatus.Processing).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, GenerationStatus.Failed).SetProperty(x => x.ErrorCode, "interrupted")
                .SetProperty(x => x.Transcript, (string?)null).SetProperty(x => x.CompletedAt, DateTime.UtcNow), stoppingToken);
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
