using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Configuration;

public sealed class LlmOptions
{
    public string Provider { get; set; } = "Mock";
    public string Model { get; set; } = "";
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/";
    public double Temperature { get; set; } = 0.2;
    public int MaxOutputTokens { get; set; } = 8192;
    public int? ThinkingBudget { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
}
public sealed class GenerationOptions
{
    public int MinTranscriptChars { get; set; } = 500;
    public int MaxTranscriptChars { get; set; } = 200000;
    public int MaxInputTokens { get; set; } = 100000;
    public int QuestionCount { get; set; } = 5;
    public int OptionsPerQuestion { get; set; } = 4;
    public int MaxExcerptWords { get; set; } = 40;
    public int MaxConcurrentJobs { get; set; } = 1;
    public int MaxPendingJobs { get; set; } = 20;
}
public sealed class OptionsValidator : IValidateOptions<LlmOptions>, IValidateOptions<GenerationOptions>
{
    public ValidateOptionsResult Validate(string? name, LlmOptions o)
    {
        if (o.Provider is not ("Mock" or "Gemini")) return ValidateOptionsResult.Fail("Llm:Provider must be Mock or Gemini.");
        if (o.Provider == "Gemini" && (string.IsNullOrWhiteSpace(o.ApiKey) || string.IsNullOrWhiteSpace(o.Model)))
            return ValidateOptionsResult.Fail("Llm:ApiKey and Llm:Model are required when Llm:Provider=Gemini. Set them with dotnet user-secrets or environment variables (Llm__ApiKey).");
        if (!Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) return ValidateOptionsResult.Fail("Llm:BaseUrl must be an absolute HTTPS URL.");
        return o.TimeoutSeconds > 0 && o.MaxOutputTokens > 0 && o.Temperature is >= 0 and <= 2 && o.ThinkingBudget is null or >= -1
            ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("Invalid Llm limits.");
    }
    public ValidateOptionsResult Validate(string? name, GenerationOptions o) =>
        o.MinTranscriptChars > 0 && o.MaxTranscriptChars >= o.MinTranscriptChars && o.MaxInputTokens > 0 &&
        o.QuestionCount == 5 && o.OptionsPerQuestion == 4 && o.MaxExcerptWords is > 0 and <= 40 &&
        o.MaxConcurrentJobs is > 0 and <= 4 && o.MaxPendingJobs > 0
            ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("Invalid Generation limits; this POC supports five questions and four options.");
}
