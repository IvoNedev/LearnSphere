using System.Text.Json.Serialization;
namespace LearnSphere.Api.Domain;

public enum GenerationStatus { Queued, Processing, Succeeded, Failed }
public sealed record Summary([property: JsonRequired] string Overview, [property: JsonRequired] string[] KeyPoints);
public sealed record QuizQuestion([property: JsonRequired] string Prompt, [property: JsonRequired] string[] Options,
    [property: JsonRequired] int CorrectAnswerIndex, [property: JsonRequired] string Explanation, [property: JsonRequired] string SourceExcerpt, bool ExcerptVerified = false);
public sealed record TrainingContent(Summary Summary, QuizQuestion[] Questions);
public sealed record GenerationMetadata(string Provider, string Model, string PromptVersion, int TranscriptChars,
    int? InputTokens, int? OutputTokens, int? ThoughtTokens, long DurationMs, bool RepairAttempted, string? FinishReason = null);
public sealed record JobError(string Code, string Message, bool Retryable);
public static class JobErrors
{
    public static JobError Get(string code) => code switch
    {
        "input_too_large" => new(code, "This transcript exceeds the configured token budget. Submit a shorter transcript.", false),
        "insufficient_content" => new(code, "The transcript does not contain enough distinct facts for five meaningful questions.", false),
        "provider_invalid_output" => new(code, "The AI returned an invalid result, including after one repair attempt. Review the source and try again.", false),
        "provider_refused" => new(code, "The AI could not process this content. Review the transcript.", false),
        "provider_truncated" => new(code, "The AI response exceeded its output budget. Review the model configuration.", false),
        "provider_rate_limited" => new(code, "The AI provider is rate-limiting requests. Wait a minute and retry.", true),
        "provider_unavailable" => new(code, "The AI provider is unavailable. Please try again later.", true),
        "provider_timeout" => new(code, "The AI provider took too long. Please try again later.", true),
        "provider_auth_failed" => new(code, "The AI provider rejected the key. Check the server configuration.", false),
        "interrupted" => new(code, "Processing was interrupted by a restart. Resubmit to try again.", true),
        _ => new("internal_error", "Generation failed. Check the server configuration or contact the operator.", false)
    };
}
public sealed class Generation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Title { get; set; }
    public GenerationStatus Status { get; set; }
    public string GenerationMode { get; set; } = "mock";
    public string? Transcript { get; set; }
    public int TranscriptChars { get; set; }
    public string TranscriptSha256 { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TrainingContent? Content { get; set; }
    public GenerationMetadata? Metadata { get; set; }
    public string? ErrorCode { get; set; }
    public void Succeed(TrainingContent content, GenerationMetadata metadata)
    {
        Content = content; Metadata = metadata; Status = GenerationStatus.Succeeded; Finish();
    }
    public void Fail(string code, GenerationMetadata? metadata = null)
    {
        ErrorCode = code; Metadata = metadata; Status = GenerationStatus.Failed; Finish();
    }
    private void Finish() { Transcript = null; CompletedAt = DateTime.UtcNow; }
}
