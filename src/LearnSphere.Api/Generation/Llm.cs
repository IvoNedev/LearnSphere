using System.Text.Json;
namespace LearnSphere.Api.Generation;

public sealed record LlmMessage(string Role, string Text);
public sealed record LlmRequest(string SystemPrompt, IReadOnlyList<LlmMessage> Messages, JsonElement JsonSchema);
public enum FinishReason { Stop, MaxTokens, Safety, Other }
public sealed record LlmResponse(string? Text, FinishReason FinishReason = FinishReason.Stop, string? BlockReason = null,
    int? InputTokens = null, int? OutputTokens = null, int? ThoughtTokens = null);
public interface ILlmClient
{
    Task<int?> CountTokensAsync(LlmRequest request, CancellationToken ct);
    Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct);
}
public enum LlmErrorKind { RateLimited, Unavailable, Timeout, AuthFailed, BadRequest }
public sealed class LlmException(LlmErrorKind kind, TimeSpan? retryAfter = null) : Exception("AI provider request failed.")
{
    public LlmErrorKind Kind { get; } = kind;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
