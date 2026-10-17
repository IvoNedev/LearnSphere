using LearnSphere.Api.Domain;
namespace LearnSphere.Api.Contracts;

public sealed record CreateGenerationRequest(string? Title, string? Transcript, bool UseChunking = false);
public sealed record CreateGenerationResponse(Guid Id, GenerationStatus Status);
public sealed record GenerationResponse(Guid Id, string? Title, GenerationStatus Status, string GenerationMode,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? CompletedAt, Summary? Summary, QuizQuestion[]? Questions,
    GenerationMetadata? Metadata, JobError? Error);
public sealed record GenerationListItem(Guid Id, string? Title, GenerationStatus Status, string GenerationMode, DateTime CreatedAt);
public sealed record GenerationCapabilities(int MinTranscriptChars, int MaxTranscriptChars, int MaxTitleChars, int MaxRequestBodyBytes,
    int QuestionCount, int OptionsPerQuestion, int MaxExcerptWords, int PollIntervalMs, int MaxPollAttempts, int PollRequestTimeoutMs,
    bool ChunkingEnabled, int ChunkTargetChars, int MaxChunks, string GenerationMode);
