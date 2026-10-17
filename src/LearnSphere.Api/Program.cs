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
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1_048_576);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.AddOptions<LlmOptions>().BindConfiguration("Llm").ValidateOnStart();
builder.Services.AddOptions<GenerationOptions>().BindConfiguration("Generation").ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<LlmOptions>, OptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<GenerationOptions>, OptionsValidator>();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/learnsphere.db"));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? c.HttpContext.TraceIdentifier);
builder.Services.AddOpenApi();
builder.Services.AddSingleton<GenerationSignal>();
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
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
}
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    int status = ex is BadHttpRequestException bad ? (bad.StatusCode == 415 ? 400 : bad.StatusCode) : 500;
    await Results.Problem(statusCode: status, title: status == 400 ? "Invalid request body or content type." : status == 413 ? "Request body is too large." : "An unexpected server error occurred.", extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
}));
app.UseStatusCodePages(async context => await Results.Problem(statusCode: context.HttpContext.Response.StatusCode, extensions: new Dictionary<string, object?> { ["traceId"] = context.HttpContext.TraceIdentifier }).ExecuteAsync(context.HttpContext));
app.Use(async (context, next) =>
{
    if (context.Request.ContentLength > 1_048_576) { await Results.Problem(statusCode: 413, title: "Request body is too large.", extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context); return; }
    // OpenAPI Accepts metadata also participates in endpoint selection. Normalize this error
    // before a content-type mismatch can be handled by the generic /api fallback.
    if (HttpMethods.IsPost(context.Request.Method) && string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/api/v1/generations", StringComparison.OrdinalIgnoreCase) && !context.Request.HasJsonContentType())
    { await Results.Problem(statusCode: 400, title: "Use application/json.").ExecuteAsync(context); return; }
    await next(context);
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapOpenApi(); app.MapScalarApiReference(); app.MapHealthChecks("/health");
var api = app.MapGroup("/api/v1/generations");
api.MapPost("", async (HttpContext http, AppDbContext db, GenerationSignal signal, IOptions<GenerationOptions> limits, IOptions<LlmOptions> llm, CancellationToken ct) =>
{
    if (!http.Request.HasJsonContentType()) return Results.Problem(statusCode: 400, title: "Use application/json.");
    CreateGenerationRequest? request;
    try { request = await http.Request.ReadFromJsonAsync<CreateGenerationRequest>(ct); }
    catch (JsonException) { return Results.Problem(statusCode: 400, title: "Malformed JSON."); }
    var errors = new Dictionary<string, string[]>();
    string transcript = request?.Transcript?.Trim() ?? "";
    if (transcript.Length < limits.Value.MinTranscriptChars || transcript.Length > limits.Value.MaxTranscriptChars) errors["transcript"] = [$"Transcript must contain {limits.Value.MinTranscriptChars:N0}–{limits.Value.MaxTranscriptChars:N0} characters."];
    if (request?.Title?.Length > 200) errors["title"] = ["Title must be at most 200 characters."];
    if (errors.Count != 0) return Results.ValidationProblem(errors);
    await signal.Admission.WaitAsync(ct);
    try
    {
        if (await db.Generations.CountAsync(x => x.Status == GenerationStatus.Queued || x.Status == GenerationStatus.Processing, ct) >= limits.Value.MaxPendingJobs)
        { http.Response.Headers.RetryAfter = "5"; return Results.Problem(statusCode: 503, title: "The generation queue is full. Try again shortly."); }
        var job = new Generation { Title = request?.Title?.Trim(), Transcript = transcript, TranscriptChars = transcript.Length,
            TranscriptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transcript))), GenerationMode = llm.Value.Provider == "Mock" ? "mock" : "live" };
        db.Generations.Add(job); await db.SaveChangesAsync(ct); signal.Pulse();
        return Results.Accepted($"/api/v1/generations/{job.Id}", new CreateGenerationResponse(job.Id, GenerationStatus.Queued));
    }
    finally { signal.Admission.Release(); }
}).Accepts<CreateGenerationRequest>("application/json").Produces<CreateGenerationResponse>(202).ProducesValidationProblem().ProducesProblem(413).ProducesProblem(503);
api.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
{
    var job = await db.Generations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    return job is null ? Results.Problem(statusCode: 404, title: "Generation not found.") : Results.Ok(new GenerationResponse(
        job.Id, job.Title, job.Status, job.GenerationMode, job.CreatedAt, job.StartedAt, job.CompletedAt,
        job.Content?.Summary, job.Content?.Questions, job.Metadata, job.ErrorCode is null ? null : JobErrors.Get(job.ErrorCode)));
}).Produces<GenerationResponse>().ProducesProblem(404);
api.MapGet("", async (HttpRequest request, AppDbContext db, CancellationToken ct) =>
{
    int limit = 20;
    if (request.Query.ContainsKey("limit") && (!int.TryParse(request.Query["limit"], out limit) || limit is < 1 or > 100)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["limit"] = ["Limit must be between 1 and 100."] });
    return Results.Ok(await db.Generations.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(limit).Select(x => new GenerationListItem(x.Id, x.Title, x.Status, x.GenerationMode, x.CreatedAt)).ToArrayAsync(ct));
}).Produces<GenerationListItem[]>().ProducesValidationProblem();
api.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
{
    var deleted = await db.Generations.Where(x => x.Id == id && x.Status != GenerationStatus.Processing).ExecuteDeleteAsync(ct);
    if (deleted == 1) return Results.NoContent();
    return await db.Generations.AnyAsync(x => x.Id == id, ct) ? Results.Problem(statusCode: 409, title: "Wait for processing to finish before deleting.") : Results.Problem(statusCode: 404, title: "Generation not found.");
}).Produces(204).ProducesProblem(404).ProducesProblem(409);
app.Map("/api/{**rest}", () => Results.Problem(statusCode: 404, title: "API route not found."));
app.MapFallbackToFile("index.html");
await app.RunAsync();
return 0;

public partial class Program;
