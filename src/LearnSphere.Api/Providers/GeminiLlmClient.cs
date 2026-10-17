using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using LearnSphere.Api.Configuration;
using LearnSphere.Api.Generation;
using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Providers;

public sealed class GeminiLlmClient(HttpClient http, IOptions<LlmOptions> options) : ILlmClient
{
    private readonly LlmOptions config = options.Value;
    private JsonObject Body(LlmRequest request)
    {
        var messages = new JsonArray();
        foreach (var m in request.Messages) messages.Add(new JsonObject { ["role"] = m.Role, ["parts"] = new JsonArray(new JsonObject { ["text"] = m.Text }) });
        return new JsonObject { ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.SystemPrompt }) }, ["contents"] = messages };
    }
    public async Task<int?> CountTokensAsync(LlmRequest request, CancellationToken ct)
    {
        try
        {
            // CountTokensRequest wraps the full GenerateContentRequest when system instructions are present.
            var inner = Body(request); inner["model"] = "models/" + config.Model;
            using var doc = await SendAsync("countTokens", new JsonObject { ["generateContentRequest"] = inner }, ct);
            return Int(doc.RootElement, "totalTokens");
        }
        catch (LlmException) { return null; }
        catch (JsonException) { return null; }
    }
    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct)
    {
        var body = Body(request);
        var generation = new JsonObject { ["temperature"] = config.Temperature, ["maxOutputTokens"] = config.MaxOutputTokens,
            ["responseMimeType"] = "application/json", ["responseJsonSchema"] = JsonNode.Parse(request.JsonSchema.GetRawText()) };
        if (config.ThinkingBudget.HasValue) generation["thinkingConfig"] = new JsonObject { ["thinkingBudget"] = config.ThinkingBudget.Value };
        body["generationConfig"] = generation;
        try
        {
            using var doc = await SendAsync("generateContent", body, ct); var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(null, FinishReason.Other);
            string? blocked = root.TryGetProperty("promptFeedback", out var feedback) ? String(feedback, "blockReason") : null;
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return new(null, FinishReason.Other, blocked);
            var candidate = candidates[0]; string text = "";
            if (candidate.ValueKind != JsonValueKind.Object) return new(null, FinishReason.Other, blocked);
            if (candidate.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
                text = string.Concat(parts.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object && (!p.TryGetProperty("thought", out var thought) || thought.ValueKind != JsonValueKind.True)).Select(p => String(p, "text") ?? ""));
            var finish = String(candidate, "finishReason") switch { "STOP" => FinishReason.Stop, "MAX_TOKENS" => FinishReason.MaxTokens, "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" => FinishReason.Safety, _ => FinishReason.Other };
            var usage = root.TryGetProperty("usageMetadata", out var u) ? u : default;
            return new(text, finish, blocked, Int(usage, "promptTokenCount"), Int(usage, "candidatesTokenCount"), Int(usage, "thoughtsTokenCount"));
        }
        catch (JsonException) { return new(null, FinishReason.Other); }
    }
    private async Task<JsonDocument> SendAsync(string operation, JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(config.BaseUrl), $"v1beta/models/{Uri.EscapeDataString(config.Model)}:{operation}"));
        request.Headers.Add("x-goog-api-key", config.ApiKey);
        request.Content = JsonContent.Create(body);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var kind = response.StatusCode switch { HttpStatusCode.TooManyRequests => LlmErrorKind.RateLimited, HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => LlmErrorKind.AuthFailed, HttpStatusCode.BadRequest or HttpStatusCode.NotFound => LlmErrorKind.BadRequest, _ => LlmErrorKind.Unavailable };
                var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                throw new LlmException(kind, retry);
            }
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException) { throw new LlmException(LlmErrorKind.Unavailable); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new LlmException(LlmErrorKind.Timeout); }
    }
    private static int? Int(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static string? String(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
