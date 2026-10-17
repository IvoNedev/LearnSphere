using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LearnSphere.Api.Configuration;
using LearnSphere.Api.Contracts;
using LearnSphere.Api.Domain;
using LearnSphere.Api.Generation;
using LearnSphere.Api.Jobs;
using LearnSphere.Api.Persistence;
using LearnSphere.Api.Providers;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

if (args.Contains("--healthcheck"))
{
    try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) }; return (await client.GetAsync("http://localhost:8080/health")).IsSuccessStatusCode ? 0 : 1; }
    catch (HttpRequestException) { return 1; }
    catch (TaskCanceledException) { return 1; }
}
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel((context, o) => o.Limits.MaxRequestBodySize = (context.Configuration.GetSection("Generation").Get<GenerationOptions>() ?? new()).MaxRequestBodyBytes);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.AddOptions<LlmOptions>().BindConfiguration("Llm").ValidateOnStart();
builder.Services.AddOptions<GenerationOptions>().BindConfiguration("Generation").ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<LlmOptions>, OptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<GenerationOptions>, OptionsValidator>();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/learnsphere.db"));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
{
    c.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? c.HttpContext.TraceIdentifier;
    if (!c.ProblemDetails.Extensions.ContainsKey("code")) c.ProblemDetails.Extensions["code"] = c.ProblemDetails is HttpValidationProblemDetails ? "validation_failed" : c.ProblemDetails.Status switch { 400 => "invalid_request", 404 => "not_found", 409 => "conflict", 413 => "request_body_too_large", 503 => "service_unavailable", _ => "internal_error" };
});
builder.Services.AddOpenApi();
builder.Services.AddSingleton<GenerationSignal>();
builder.Services.AddSingleton<ResultJournal>();
builder.Services.AddScoped<TrainingContentService>();
builder.Services.AddScoped<GenerationProcessor>();
builder.Services.AddSingleton<MockLlmClient>();
builder.Services.AddHttpClient<GeminiLlmClient>((sp, http) => http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<LlmOptions>>().Value.TimeoutSeconds)).RemoveAllLoggers();
builder.Services.AddScoped<ILlmClient>(sp => sp.GetRequiredService<IOptions<LlmOptions>>().Value.Provider == "Mock" ? sp.GetRequiredService<MockLlmClient>() : sp.GetRequiredService<GeminiLlmClient>());
builder.Services.AddHostedService<GenerationWorker>();
var app = builder.Build();
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
using (var scope = app.Services.CreateScope())
{
    _ = scope.ServiceProvider.GetRequiredService<IOptions<LlmOptions>>().Value;
    _ = scope.ServiceProvider.GetRequiredService<IOptions<GenerationOptions>>().Value;
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    // Additive upgrade for existing POC volumes; no existing transcript/result columns are changed.
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "GenerationIntents" (
          "GenerationId" TEXT NOT NULL CONSTRAINT "PK_GenerationIntents" PRIMARY KEY,
          "Provider" TEXT NOT NULL, "Model" TEXT NOT NULL, "UseChunking" INTEGER NOT NULL,
          FOREIGN KEY ("GenerationId") REFERENCES "Generations" ("Id") ON DELETE CASCADE);
        """);
}
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    bool storageFailure = ex is Microsoft.Data.Sqlite.SqliteException or DbUpdateException or IOException or UnauthorizedAccessException;
    int status = storageFailure ? 503 : ex is BadHttpRequestException bad ? (bad.StatusCode == 415 ? 400 : bad.StatusCode) : 500;
    await Results.Problem(statusCode: status, title: storageFailure ? "Storage is unavailable. Try this step again later." : status == 400 ? "Invalid request body or content type." : status == 413 ? "Request body is too large." : "An unexpected server error occurred.", extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier, ["code"] = storageFailure ? "storage_unavailable" : status == 400 ? "invalid_request" : status == 413 ? "request_body_too_large" : "internal_error" }).ExecuteAsync(context);
}));
app.UseStatusCodePages(async context => await Results.Problem(statusCode: context.HttpContext.Response.StatusCode, extensions: new Dictionary<string, object?> { ["traceId"] = context.HttpContext.TraceIdentifier }).ExecuteAsync(context.HttpContext));
app.Use(async (context, next) =>
{
    var bodyLimit = context.RequestServices.GetRequiredService<IOptions<GenerationOptions>>().Value.MaxRequestBodyBytes;
    if (context.Request.ContentLength > bodyLimit) { await Results.Problem(statusCode: 413, title: $"Request body exceeds {bodyLimit:N0} bytes.", extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier, ["code"] = "request_body_too_large" }).ExecuteAsync(context); return; }
    // OpenAPI Accepts metadata also participates in endpoint selection. Normalize this error
    // before a content-type mismatch can be handled by the generic /api fallback.
    if (HttpMethods.IsPost(context.Request.Method) && string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/api/v1/generations", StringComparison.OrdinalIgnoreCase) && !context.Request.HasJsonContentType())
    { await Results.Problem(statusCode: 400, title: "Use application/json.", extensions: new Dictionary<string, object?> { ["code"] = "unsupported_content_type" }).ExecuteAsync(context); return; }
    await next(context);
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapOpenApi(); app.MapScalarApiReference(); app.MapHealthChecks("/health");
app.MapGet("/api/v1/capabilities", (IOptions<GenerationOptions> options, IOptions<LlmOptions> llm) =>
{
    var o = options.Value;
    return new GenerationCapabilities(o.MinTranscriptChars, o.MaxTranscriptChars, o.MaxTitleChars, o.MaxRequestBodyBytes, o.QuestionCount, o.OptionsPerQuestion,
        o.MaxExcerptWords, o.PollIntervalMs, o.MaxPollAttempts, o.PollRequestTimeoutMs, o.ChunkingEnabled, o.ChunkTargetChars, o.MaxChunks, llm.Value.Provider == "Mock" ? "mock" : "live");
});
var api = app.MapGroup("/api/v1/generations");
api.MapPost("", async (HttpContext http, AppDbContext db, GenerationSignal signal, IOptions<GenerationOptions> limits, IOptions<LlmOptions> llm, CancellationToken ct) =>
{
    if (!http.Request.HasJsonContentType()) return Results.Problem(statusCode: 400, title: "Use application/json.");
    CreateGenerationRequest? request;
    try { request = await http.Request.ReadFromJsonAsync<CreateGenerationRequest>(ct); }
    catch (JsonException) { return Results.Problem(statusCode: 400, title: "Malformed JSON.", extensions: new Dictionary<string, object?> { ["code"] = "invalid_json" }); }
    var errors = new Dictionary<string, string[]>();
    string transcript = request?.Transcript?.Trim() ?? "";
    if (transcript.Length < limits.Value.MinTranscriptChars || transcript.Length > limits.Value.MaxTranscriptChars) errors["transcript"] = [$"Transcript must contain {limits.Value.MinTranscriptChars:N0}–{limits.Value.MaxTranscriptChars:N0} characters."];
    if (request?.Title?.Length > limits.Value.MaxTitleChars) errors["title"] = [$"Title must be at most {limits.Value.MaxTitleChars} characters."];
    if (request?.UseChunking == true && !limits.Value.ChunkingEnabled) errors["useChunking"] = ["Section processing is disabled on this server."];
    if (errors.Count != 0) return Results.ValidationProblem(errors);
    await signal.Admission.WaitAsync(ct);
    try
    {
        if (await db.Generations.CountAsync(x => x.Status == GenerationStatus.Queued || x.Status == GenerationStatus.Processing, ct) >= limits.Value.MaxPendingJobs)
        { http.Response.Headers.RetryAfter = "5"; return Results.Problem(statusCode: 503, title: "The generation queue is full. Try again shortly.", extensions: new Dictionary<string, object?> { ["code"] = "queue_full" }); }
        var job = new Generation
        {
            Title = request?.Title?.Trim(),
            Transcript = transcript,
            TranscriptChars = transcript.Length,
            TranscriptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transcript))),
            GenerationMode = llm.Value.Provider == "Mock" ? "mock" : "live"
        };
        db.Generations.Add(job);
        db.GenerationIntents.Add(new GenerationIntent { GenerationId = job.Id, Provider = llm.Value.Provider, Model = llm.Value.Model, UseChunking = request?.UseChunking ?? false });
        await db.SaveChangesAsync(ct); signal.Pulse();
        return Results.Accepted($"/api/v1/generations/{job.Id}", new CreateGenerationResponse(job.Id, GenerationStatus.Queued));
    }
    finally { signal.Admission.Release(); }
}).Accepts<CreateGenerationRequest>("application/json").Produces<CreateGenerationResponse>(202).ProducesValidationProblem().ProducesProblem(413).ProducesProblem(503);
api.MapGet("/{id:guid}", async (Guid id, AppDbContext db, ResultJournal journal, CancellationToken ct) =>
{
    var job = await db.Generations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    if (job is not null && journal.IsFailed(id)) { job.Fail("result_persistence_failed", journal.Read(id)?.Metadata); }
    return job is null ? Results.Problem(statusCode: 404, title: "Generation not found.") : Results.Ok(new GenerationResponse(
        job.Id, job.Title, job.Status, job.GenerationMode, job.CreatedAt, job.StartedAt, job.CompletedAt,
        job.Content?.Summary, job.Content?.Questions, job.Metadata, job.ErrorCode is null ? null : JobErrors.Get(job.ErrorCode) with { RetryAfterSeconds = job.Metadata?.RetryAfterSeconds }));
}).Produces<GenerationResponse>().ProducesProblem(404);
api.MapGet("", async (HttpRequest request, AppDbContext db, CancellationToken ct) =>
{
    int limit = 20;
    if (request.Query.ContainsKey("limit") && (!int.TryParse(request.Query["limit"], out limit) || limit is < 1 or > 100)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["limit"] = ["Limit must be between 1 and 100."] });
    return Results.Ok(await db.Generations.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(limit).Select(x => new GenerationListItem(x.Id, x.Title, x.Status, x.GenerationMode, x.CreatedAt)).ToArrayAsync(ct));
}).Produces<GenerationListItem[]>().ProducesValidationProblem();
api.MapPost("/{id:guid}/retry", async (Guid id, GenerationProcessor processor, AppDbContext db, ResultJournal journal, GenerationSignal signal, CancellationToken ct) =>
{
    await signal.Admission.WaitAsync(ct);
    try
    {
        if (!await db.Generations.AnyAsync(x => x.Id == id, ct)) return Results.Problem(statusCode: 404, title: "Generation not found.");
        if (journal.Read(id) is null) return Results.Problem(statusCode: 409, title: "No result-save checkpoint is available. This endpoint never reruns the AI.");
        if (!await processor.ResumePersistenceAsync(id, ct)) return Results.Problem(statusCode: 503, title: "Result storage is still unavailable. Retry the save later.", extensions: new Dictionary<string, object?> { ["code"] = "result_persistence_failed" });
        var job = await db.Generations.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return Results.Ok(new CreateGenerationResponse(id, job.Status));
    }
    finally { signal.Admission.Release(); }
}).Produces<CreateGenerationResponse>().ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
api.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ResultJournal journal, CancellationToken ct) =>
{
    var deleted = await db.Generations.Where(x => x.Id == id && x.Status != GenerationStatus.Processing).ExecuteDeleteAsync(ct);
    if (deleted == 1) { journal.Complete(id); return Results.NoContent(); }
    return await db.Generations.AnyAsync(x => x.Id == id, ct) ? Results.Problem(statusCode: 409, title: "Wait for processing to finish before deleting.") : Results.Problem(statusCode: 404, title: "Generation not found.");
}).Produces(204).ProducesProblem(404).ProducesProblem(409);
app.Map("/api/{**rest}", () => Results.Problem(statusCode: 404, title: "API route not found."));
app.MapFallbackToFile("index.html");
await app.RunAsync();
return 0;

public partial class Program;
