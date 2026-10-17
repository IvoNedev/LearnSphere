using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LearnSphere.Api.Configuration;
using LearnSphere.Api.Contracts;
using LearnSphere.Api.Domain;
using LearnSphere.Api.Jobs;
using LearnSphere.Api.Persistence;
using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Endpoints;

public static class GenerationEndpoints
{
    public static void MapGenerationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/capabilities", (IOptions<GenerationOptions> options, IOptions<LlmOptions> llm) =>
        {
            var o = options.Value;
            return new GenerationCapabilities(
                o.MinTranscriptChars, o.MaxTranscriptChars, o.MaxTitleChars, o.MaxRequestBodyBytes,
                o.QuestionCount, o.OptionsPerQuestion, o.MaxExcerptWords,
                o.PollIntervalMs, o.MaxPollAttempts, o.PollRequestTimeoutMs,
                o.ChunkingEnabled, o.ChunkTargetChars, o.MaxChunks,
                llm.Value.Provider == "Mock" ? "mock" : "live");
        });
        var api = app.MapGroup("/api/v1/generations");
        api.MapPost("", CreateAsync)
            .Accepts<CreateGenerationRequest>("application/json")
            .Produces<CreateGenerationResponse>(202)
            .ProducesValidationProblem()
            .ProducesProblem(413)
            .ProducesProblem(503);
        api.MapGet("/{id:guid}", GetAsync)
            .Produces<GenerationResponse>()
            .ProducesProblem(404);
        api.MapGet("", ListAsync)
            .Produces<GenerationListItem[]>()
            .ProducesValidationProblem();
        api.MapPost("/{id:guid}/retry", RetrySaveAsync)
            .Produces<CreateGenerationResponse>()
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(503);
        api.MapDelete("/{id:guid}", DeleteAsync)
            .Produces(204)
            .ProducesProblem(404)
            .ProducesProblem(409);

    }
    private static async Task<IResult> CreateAsync(HttpContext http,
        GenerationStore store,
        GenerationSignal signal,
        IOptions<GenerationOptions> limits,
        IOptions<LlmOptions> llm,
        CancellationToken ct)
    {
        if (!http.Request.HasJsonContentType()) return Results.Problem(statusCode: 400, title: "Use application/json.");
        CreateGenerationRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<CreateGenerationRequest>(ct);
        }
        catch (JsonException)
        {
            return Results.Problem(statusCode: 400,
                title: "Malformed JSON.",
                extensions: new Dictionary<string, object?>
                 { ["code"] = "invalid_json" });
        }
        string transcript = request?.Transcript?.Trim() ?? "";
        var errors = GenerationRequestValidator.Validate(request, transcript, limits.Value);
        if (errors.Count != 0) return Results.ValidationProblem(errors);
        await signal.Admission.WaitAsync(ct);
        try
        {
            if (await store.CountPendingAsync(ct) >= limits.Value.MaxPendingJobs)
            {
                http.Response.Headers.RetryAfter = "5";
                return Results.Problem(statusCode: 503,
                    title: "The generation queue is full. Try again shortly.",
                    extensions: new Dictionary<string, object?>
                     { ["code"] = "queue_full" });
            }
            var job = new LearnSphere.Api.Domain.Generation
            {
                Title = request?.Title?.Trim(),
                Transcript = transcript,
                TranscriptChars = transcript.Length,
                TranscriptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transcript))),
                GenerationMode = llm.Value.Provider == "Mock" ? "mock" : "live"
            };
            var intent = new GenerationIntent
            {
                GenerationId = job.Id,
                Provider = llm.Value.Provider,
                Model = llm.Value.Model,
                UseChunking = request?.UseChunking ?? false
            };
            await store.AddAsync(job, intent, ct);
            signal.Pulse();
            return Results.Accepted($"/api/v1/generations/{job.Id}", new CreateGenerationResponse(job.Id, GenerationStatus.Queued));
        }
        finally
        {
            signal.Admission.Release();
        }
    }

    private static async Task<IResult> GetAsync(Guid id, GenerationStore store, ResultJournal journal, CancellationToken ct)
    {
        var job = await store.FindAsync(id, ct);
        if (job is not null && journal.IsFailed(id))
        {
            job.Fail("result_persistence_failed", journal.Read(id)?.Metadata);
        }
        if (job is null)
            return Results.Problem(statusCode: 404, title: "Generation not found.");

        var error = job.ErrorCode is null ? null : JobErrors.Get(job.ErrorCode) with
        {
            RetryAfterSeconds = job.Metadata?.RetryAfterSeconds
        };
        return Results.Ok(new GenerationResponse(
            job.Id, job.Title, job.Status, job.GenerationMode,
            job.CreatedAt, job.StartedAt, job.CompletedAt,
            job.Content?.Summary, job.Content?.Questions, job.Metadata, error));
    }

    private static async Task<IResult> ListAsync(HttpRequest request, GenerationStore store, CancellationToken ct)
    {
        int limit = 20;
        if (request.Query.ContainsKey("limit") &&
            (!int.TryParse(request.Query["limit"], out limit) || limit is < 1 or > 100))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["Limit must be between 1 and 100."]
            });
        }
        return Results.Ok(await store.ListAsync(limit, ct));
    }

    private static async Task<IResult> RetrySaveAsync(Guid id,
        GenerationProcessor processor,
        GenerationStore store,
        ResultJournal journal,
        GenerationSignal signal,
        CancellationToken ct)
    {
        await signal.Admission.WaitAsync(ct);
        try
        {
            if (!await store.ExistsAsync(id, ct))
                return Results.Problem(statusCode: 404, title: "Generation not found.");
            if (journal.Read(id) is null)
            {
                return Results.Problem(statusCode: 409,
                   title: "No result-save checkpoint is available. This endpoint never reruns the AI.");
            }
            if (!await processor.ResumePersistenceAsync(id, ct))
            {
                return Results.Problem(statusCode: 503,
                   title: "Result storage is still unavailable. Retry the save later.",
                   extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "result_persistence_failed"
                    });
            }
            var job = (await store.FindAsync(id, ct))!;
            return Results.Ok(new CreateGenerationResponse(id, job.Status));
        }
        finally
        {
            signal.Admission.Release();
        }
    }

    private static async Task<IResult> DeleteAsync(Guid id, GenerationStore store, ResultJournal journal, CancellationToken ct)
    {
        var deleted = await store.DeleteCompletedAsync(id, ct);
        if (deleted == 1)
        {
            journal.Complete(id);
            return Results.NoContent();
        }
        return await store.ExistsAsync(id, ct)
            ? Results.Problem(statusCode: 409, title: "Wait for processing to finish before deleting.")
            : Results.Problem(statusCode: 404, title: "Generation not found.");
    }
}
