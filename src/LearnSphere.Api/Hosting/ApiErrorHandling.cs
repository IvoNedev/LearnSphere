using LearnSphere.Api.Configuration;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Hosting;

public static class ApiErrorHandling
{
    public static void UseApiErrorHandling(this WebApplication app)
    {
        app.UseExceptionHandler(handler => handler.Run(HandleExceptionAsync));
        app.UseStatusCodePages(async context =>
            await Results.Problem(
                statusCode: context.HttpContext.Response.StatusCode,
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = context.HttpContext.TraceIdentifier
                }).ExecuteAsync(context.HttpContext));
        app.Use(ValidateRequestAsync);
    }

    private static Task HandleExceptionAsync(HttpContext context)
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        bool storageFailure = error is Microsoft.Data.Sqlite.SqliteException
            or DbUpdateException or IOException or UnauthorizedAccessException;
        int status = storageFailure ? 503 : error is BadHttpRequestException bad
            ? (bad.StatusCode == 415 ? 400 : bad.StatusCode) : 500;
        string title = storageFailure ? "Storage is unavailable. Try this step again later." : status switch
        {
            400 => "Invalid request body or content type.",
            413 => "Request body is too large.",
            _ => "An unexpected server error occurred."
        };
        string code = storageFailure ? "storage_unavailable" : status switch
        {
            400 => "invalid_request",
            413 => "request_body_too_large",
            _ => "internal_error"
        };
        return Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = context.TraceIdentifier,
                ["code"] = code
            }).ExecuteAsync(context);
    }

    private static async Task ValidateRequestAsync(HttpContext context, RequestDelegate next)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<GenerationOptions>>().Value;
        if (context.Request.ContentLength > limits.MaxRequestBodyBytes)
        {
            await Results.Problem(
                statusCode: 413,
                title: $"Request body exceeds {limits.MaxRequestBodyBytes:N0} bytes.",
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = context.TraceIdentifier,
                    ["code"] = "request_body_too_large"
                }).ExecuteAsync(context);
            return;
        }

        // Normalize content-type failures before endpoint selection can use the /api fallback.
        bool createsGeneration = HttpMethods.IsPost(context.Request.Method)
            && string.Equals(context.Request.Path.Value?.TrimEnd('/'),
                "/api/v1/generations", StringComparison.OrdinalIgnoreCase);
        if (createsGeneration && !context.Request.HasJsonContentType())
        {
            await Results.Problem(statusCode: 400, title: "Use application/json.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "unsupported_content_type"
                }).ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}
