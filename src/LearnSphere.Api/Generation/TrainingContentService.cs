using System.Diagnostics;
using System.Text.Json;
using LearnSphere.Api.Configuration;
using LearnSphere.Api.Domain;
using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Generation;

public sealed record GenerationResult(TrainingContent? Content, GenerationMetadata Metadata, string? ErrorCode);
public sealed class TrainingContentService(ILlmClient client, IOptions<LlmOptions> llmOptions, IOptions<GenerationOptions> options, ILogger<TrainingContentService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<GenerationResult> GenerateAsync(string transcript, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew(); var config = llmOptions.Value; var limits = options.Value;
        bool repaired = false; int? input = null, output = null, thoughts = null; string? finishReason = null;
        GenerationResult Result(string? code, TrainingContent? content = null) => new(content,
            new(config.Provider, config.Provider == "Mock" ? "deterministic-v1" : config.Model, Prompts.Version, transcript.Length, input, output, thoughts, sw.ElapsedMilliseconds, repaired, finishReason), code);
        var request = Prompts.Create(transcript);
        // One overall job budget also bounds countTokens plus any schema repair.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds * 3));
        try
        {
            var count = await client.CountTokensAsync(request, deadline.Token);
            if (count is null) logger.LogWarning("Token count unavailable; conservative estimate used. Provider={Provider}", config.Provider);
            // Four tokens per UTF-16 code unit is deliberately conservative for unknown tokenizers.
            int budget = count ?? checked((transcript.Length + request.SystemPrompt.Length + 1000) * 4);
            if (budget > limits.MaxInputTokens) return Result("input_too_large");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var response = await client.GenerateAsync(request, deadline.Token);
                finishReason = response.FinishReason.ToString();
                input = Add(input, response.InputTokens); output = Add(output, response.OutputTokens); thoughts = Add(thoughts, response.ThoughtTokens);
                if (!string.IsNullOrEmpty(response.BlockReason) || response.FinishReason == FinishReason.Safety) return Result("provider_refused");
                if (response.FinishReason == FinishReason.MaxTokens) return Result("provider_truncated");
                if (response.FinishReason != FinishReason.Stop || string.IsNullOrWhiteSpace(response.Text)) return Result("provider_invalid_output");
                ModelOutput? parsed = null; IReadOnlyList<string> errors;
                try { parsed = JsonSerializer.Deserialize<ModelOutput>(response.Text, Json); errors = TrainingContentValidator.Validate(parsed, limits); }
                catch (JsonException) { errors = ["Response is not valid JSON matching the schema."]; }
                if (parsed?.Outcome == "insufficient_content") return Result("insufficient_content");
                if (errors.Count == 0)
                {
                    var normalized = ExcerptVerifier.Normalize(transcript);
                    var questions = parsed!.Questions!.Select(q => q with { ExcerptVerified = ExcerptVerifier.IsVerified(q.SourceExcerpt, normalized) }).ToArray();
                    return Result(null, new(parsed.Summary!, questions));
                }
                if (attempt == 1) return Result("provider_invalid_output");
                repaired = true;
                request = request with { Messages = [.. request.Messages, new("model", response.Text), new("user", "Your previous output was invalid: " + string.Join("; ", errors) + ". Return corrected JSON only, grounded in the original transcript.")] };
                // Repair input includes previous output; enforce the budget again.
                var repairCount = await client.CountTokensAsync(request, deadline.Token);
                if ((repairCount ?? request.Messages.Sum(m => m.Text.Length) * 4 + request.SystemPrompt.Length * 4 + 4000) > limits.MaxInputTokens) return Result("input_too_large");
            }
        }
        catch (LlmException ex)
        {
            return Result(ex.Kind switch { LlmErrorKind.RateLimited => "provider_rate_limited", LlmErrorKind.AuthFailed => "provider_auth_failed", LlmErrorKind.Timeout => "provider_timeout", LlmErrorKind.Unavailable => "provider_unavailable", _ => "internal_error" });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Result("provider_timeout"); }
        return Result("provider_invalid_output");
    }
    private static int? Add(int? a, int? b) => a.HasValue || b.HasValue ? (a ?? 0) + (b ?? 0) : null;
}
