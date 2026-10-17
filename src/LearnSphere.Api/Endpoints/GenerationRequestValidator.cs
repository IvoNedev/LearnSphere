using LearnSphere.Api.Configuration;
using LearnSphere.Api.Contracts;

namespace LearnSphere.Api.Endpoints;

internal static class GenerationRequestValidator
{
    public static Dictionary<string, string[]> Validate(CreateGenerationRequest? request, string transcript, GenerationOptions limits)
    {
        var errors = new Dictionary<string, string[]>();
        if (transcript.Length < limits.MinTranscriptChars || transcript.Length > limits.MaxTranscriptChars)
            errors["transcript"] = [$"Transcript must contain {limits.MinTranscriptChars:N0}–{limits.MaxTranscriptChars:N0} characters."];
        if (request?.Title?.Length > limits.MaxTitleChars)
            errors["title"] = [$"Title must be at most {limits.MaxTitleChars} characters."];
        if (request?.UseChunking == true && !limits.ChunkingEnabled)
            errors["useChunking"] = ["Section processing is disabled on this server."];
        return errors;
    }
}
