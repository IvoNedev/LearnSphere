using System.Text.Json.Serialization;
namespace LearnSphere.Api.Domain;

public enum GenerationStatus { Queued, Processing, Succeeded, Failed }
public sealed record Summary([property: JsonRequired] string Overview, [property: JsonRequired] string[] KeyPoints);
public sealed record QuizQuestion([property: JsonRequired] string Prompt, [property: JsonRequired] string[] Options,
    [property: JsonRequired] int CorrectAnswerIndex, [property: JsonRequired] string Explanation, [property: JsonRequired] string SourceExcerpt, bool ExcerptVerified = false);
public sealed record TrainingContent(Summary Summary, QuizQuestion[] Questions);
public sealed record GenerationMetadata(string Provider, string Model, string PromptVersion, int TranscriptChars,
    int? InputTokens, int? OutputTokens, int? ThoughtTokens, long DurationMs, bool RepairAttempted, string? FinishReason = null,
    int ChunkCount = 1, int? RetryAfterSeconds = null);
public sealed record JobError(string Code, string Message, bool Retryable, string? ResumeStage = null, int? RetryAfterSeconds = null);
public static class JobErrors
{
    public static JobError Get(string code) => code switch
    {
        "input_too_large" => new(code, "This transcript exceeds the configured token budget. Submit a shorter transcript.", false),
        "insufficient_content" => new(code, "The transcript does not contain enough distinct facts for five meaningful questions.", false),
        "provider_invalid_output" => new(code, "The AI response did not match the required format. Review the source and try again.", false),
        "provider_invalid_response" => new(code, "The AI provider returned an unreadable response. Try again later.", true),
        "provider_request_invalid" => new(code, "The AI provider rejected the request configuration. Check model capabilities and server settings.", false),
        "provider_model_not_found" => new(code, "The configured AI model was not found or is unavailable to this account. Check the model setting.", false),
        "token_count_unavailable" => new(code, "The AI provider did not report the input token count. Generation stopped before sending an unbudgeted request.", true),
        "provider_configuration_changed" => new(code, "The provider or model changed after submission. Resubmit explicitly using the current configuration.", false),
        "result_persistence_failed" => new(code, "Generation finished, but saving its result failed. Retry the save step; the AI will not be called again.", true, "PersistResult"),
        "chunk_limit_exceeded" => new(code, "The transcript requires more sections than the configured limit. Use a shorter transcript or adjust section settings.", false),
        "chunking_disabled" => new(code, "Section processing was disabled after submission. Resubmit using the current settings.", false),
        "result_checkpoint_unavailable" => new(code, "The saved result checkpoint cannot be read. No AI call was repeated. Restore storage or resubmit explicitly.", false),
        "chunk_invalid_output" => new(code, "The AI returned invalid section notes. No incomplete summary or quiz was published.", false),
        "provider_refused" => new(code, "The AI could not process this content. Review the transcript.", false),
        "provider_truncated" => new(code, "The AI response exceeded its output budget. Review the model configuration.", false),
        "provider_rate_limited" => new(code, "The AI provider's request or quota limit was reached. Retry after the indicated delay or check the project's quota.", true),
        "provider_unavailable" => new(code, "The AI provider is unavailable. Please try again later.", true),
        "provider_timeout" => new(code, "The AI provider took too long. Please try again later.", true),
        "provider_auth_failed" => new(code, "The AI provider rejected authentication or access. Check the key, project permissions, and model access.", false),
        "interrupted" => new(code, "Processing was interrupted by a restart. Resubmit to try again.", true),
        _ => new("internal_error", "Generation failed. Check the server configuration or contact the operator.", false)
    };
}
public sealed class GenerationIntent
{
    public Guid GenerationId { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public bool UseChunking { get; set; }
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
        Content = content; Metadata = metadata; ErrorCode = null; Status = GenerationStatus.Succeeded; Finish();
    }
    public void Fail(string code, GenerationMetadata? metadata = null)
    {
        ErrorCode = code; Content = null; Metadata = metadata; Status = GenerationStatus.Failed; Finish();
    }
    private void Finish() { Transcript = null; CompletedAt = DateTime.UtcNow; }
}
