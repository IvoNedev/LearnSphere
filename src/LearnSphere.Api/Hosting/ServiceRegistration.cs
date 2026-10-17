using System.Diagnostics;
using LearnSphere.Api.Configuration;
using LearnSphere.Api.Generation;
using LearnSphere.Api.Jobs;
using LearnSphere.Api.Persistence;
using LearnSphere.Api.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Hosting;

public static class ServiceRegistration
{
    public static void AddLearnSphere(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel((context, server) =>
        {
            var limits = context.Configuration.GetSection("Generation").Get<GenerationOptions>() ?? new();
            server.Limits.MaxRequestBodySize = limits.MaxRequestBodyBytes;
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            options.ThrowOnBadRequest = true);
        builder.Services.AddOptions<LlmOptions>().BindConfiguration("Llm").ValidateOnStart();
        builder.Services.AddOptions<GenerationOptions>().BindConfiguration("Generation").ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<LlmOptions>, OptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<GenerationOptions>, OptionsValidator>();
        builder.Services.AddScoped<GenerationStore>();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(
            builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/learnsphere.db"));
        builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
        {
            c.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? c.HttpContext.TraceIdentifier;
            if (!c.ProblemDetails.Extensions.ContainsKey("code"))
            {
                c.ProblemDetails.Extensions["code"] = c.ProblemDetails is HttpValidationProblemDetails
                    ? "validation_failed"
                    : c.ProblemDetails.Status switch
                    {
                        400 => "invalid_request",
                        404 => "not_found",
                        409 => "conflict",
                        413 => "request_body_too_large",
                        503 => "service_unavailable",
                        _ => "internal_error"
                    };
            }
        });
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton<GenerationSignal>();
        builder.Services.AddSingleton<ResultJournal>();
        builder.Services.AddScoped<TrainingContentService>();
        builder.Services.AddScoped<GenerationProcessor>();
        builder.Services.AddSingleton<MockLlmClient>();
        builder.Services.AddHttpClient<GeminiLlmClient>((services, http) =>
        {
            var options = services.GetRequiredService<IOptions<LlmOptions>>().Value;
            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        }).RemoveAllLoggers();
        builder.Services.AddScoped<ILlmClient>(services =>
            services.GetRequiredService<IOptions<LlmOptions>>().Value.Provider == "Mock"
                ? services.GetRequiredService<MockLlmClient>()
                : services.GetRequiredService<GeminiLlmClient>());
        builder.Services.AddHostedService<GenerationWorker>();

    }
}
