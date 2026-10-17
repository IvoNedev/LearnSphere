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
    public async Task<GenerationResult> GenerateAsync(string transcript, CancellationToken ct, bool useChunking = false)
    {
        var sw = Stopwatch.StartNew(); var config = llmOptions.Value; var limits = options.Value;
        bool repaired = false; int? input = null, output = null, thoughts = null; string? finishReason = null; int chunkCount = 1; int? retryAfter = null;
        GenerationResult Result(string? code, TrainingContent? content = null) => new(content,
            new(config.Provider, config.Provider == "Mock" ? "deterministic-v1" : config.Model, Prompts.Version, transcript.Length, input, output, thoughts, sw.ElapsedMilliseconds, repaired, finishReason, chunkCount, retryAfter), code);
        var request = Prompts.Create(transcript, limits.MaxExcerptWords);
        // One overall job budget also bounds countTokens plus any schema repair.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds * 3));
        try
        {
            if (useChunking && !limits.ChunkingEnabled) return Result("chunking_disabled");
            if (useChunking && transcript.Length > limits.ChunkTargetChars)
            {
                var chunks = TranscriptSections.Split(transcript, limits.ChunkTargetChars, limits.MaxChunks);
                chunkCount = chunks.Count;
                var notes = new List<SectionNotes>();
                foreach (var chunk in chunks)
                {
                    var mapRequest = new LlmRequest(TranscriptSections.SystemPrompt(limits.MaxExcerptWords), [new("user", $"<transcript>\n{chunk}\n</transcript>")], TranscriptSections.Schema);
                    await CheckBudget(mapRequest, deadline.Token);
                    var mapResponse = await client.GenerateAsync(mapRequest, deadline.Token);
                    input = Add(input, mapResponse.InputTokens); output = Add(output, mapResponse.OutputTokens); thoughts = Add(thoughts, mapResponse.ThoughtTokens);
                    var issue = ResponseError(mapResponse); if (issue is not null) return Result(issue);
                    SectionNotes? parsedNotes;
                    try { parsedNotes = JsonSerializer.Deserialize<SectionNotes>(mapResponse.Text!, Json); }
                    catch (JsonException) { return Result("chunk_invalid_output"); }
                    if (!TranscriptSections.Valid(parsedNotes, chunk, limits.MaxExcerptWords)) return Result("chunk_invalid_output");
                    notes.Add(parsedNotes!);
                }
                request = Prompts.Create("Source section notes follow. Use only these facts, and quote sourceExcerpt values verbatim as evidence.\n" + JsonSerializer.Serialize(notes, Json), limits.MaxExcerptWords);
            }
            await CheckBudget(request, deadline.Token);
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
                await CheckBudget(request, deadline.Token);
            }
        }
        catch (LlmException ex)
        {
            retryAfter = ex.RetryAfter.HasValue ? Math.Max(0, (int)Math.Ceiling(ex.RetryAfter.Value.TotalSeconds)) : null;
            return Result(ex.Kind switch { LlmErrorKind.RateLimited => "provider_rate_limited", LlmErrorKind.AuthFailed => "provider_auth_failed", LlmErrorKind.Timeout => "provider_timeout", LlmErrorKind.Unavailable => "provider_unavailable", LlmErrorKind.BadRequest => "provider_request_invalid", LlmErrorKind.ModelNotFound => "provider_model_not_found", LlmErrorKind.InvalidResponse => "provider_invalid_response", LlmErrorKind.TokenCountUnavailable => "token_count_unavailable", _ => "internal_error" });
        }
        catch (GenerationStepException ex) { return Result(ex.Code); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Result("provider_timeout"); }
        return Result("provider_invalid_output");

        async Task CheckBudget(LlmRequest value, CancellationToken token)
        {
            var count = await client.CountTokensAsync(value, token);
            if (count is null || count < 0) { logger.LogWarning("Provider did not report input token count. Provider={Provider}", config.Provider); throw new LlmException(LlmErrorKind.TokenCountUnavailable); }
            if (count > limits.MaxInputTokens) throw new GenerationStepException("input_too_large");
        }
    }
    private static string? ResponseError(LlmResponse response) => !string.IsNullOrEmpty(response.BlockReason) || response.FinishReason == FinishReason.Safety ? "provider_refused" : response.FinishReason == FinishReason.MaxTokens ? "provider_truncated" : response.FinishReason != FinishReason.Stop || string.IsNullOrWhiteSpace(response.Text) ? "provider_invalid_output" : null;
    private static int? Add(int? a, int? b) => a.HasValue || b.HasValue ? (a ?? 0) + (b ?? 0) : null;
}
public sealed class GenerationStepException(string code) : Exception("A generation stage failed.") { public string Code { get; } = code; }
