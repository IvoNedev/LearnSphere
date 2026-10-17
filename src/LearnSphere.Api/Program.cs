using LearnSphere.Api.Endpoints;
using LearnSphere.Api.Hosting;
using Scalar.AspNetCore;

if (args.Contains("--healthcheck"))
    return await HealthProbe.RunAsync();

var builder = WebApplication.CreateBuilder(args);
builder.AddLearnSphere();
var app = builder.Build();
await app.InitializeDatabaseAsync();
app.UseApiErrorHandling();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapHealthChecks("/health");
app.MapGenerationEndpoints();
app.Map("/api/{**rest}", () => Results.Problem(statusCode: 404, title: "API route not found."));
app.MapFallbackToFile("index.html");
await app.RunAsync();
return 0;

public partial class Program;
