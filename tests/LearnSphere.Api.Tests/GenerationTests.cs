using System.Text.Json;
using LearnSphere.Api.Configuration;
using LearnSphere.Api.Domain;
using LearnSphere.Api.Generation;
using LearnSphere.Api.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Tests;

public sealed class ScriptedLlmClient(params object[] responses) : ILlmClient
{
    public int Calls { get; private set; }
    public int? Tokens { get; set; } = 100;
    public List<LlmRequest> Requests { get; } = [];
    public Task<int?> CountTokensAsync(LlmRequest request, CancellationToken ct) => Task.FromResult(Tokens);
    public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct)
    {
        Requests.Add(request); var response = responses[Calls++];
        if (response is Exception ex) throw ex;
        return Task.FromResult((LlmResponse)response);
    }
}
public class GenerationTests
{
    public static readonly string Transcript = string.Join(" ", Enumerable.Range(1, 12).Select(i => $"Source fact number {i} states that employees must follow procedure {i} when working with customer records."));
    public static ModelOutput Valid() => new("ok", "", new("Overview", ["One", "Two", "Three"]), Enumerable.Range(0, 5).Select(i => new QuizQuestion($"Question {i}", ["Alpha", "Beta", "Gamma", "Delta"], i % 4, "Explanation", "employees must follow")).ToArray());
    public static LlmResponse Response(ModelOutput? output = null, int? tokens = null) => new(JsonSerializer.Serialize(output ?? Valid(), new JsonSerializerOptions(JsonSerializerDefaults.Web)), InputTokens: tokens, OutputTokens: tokens);
    public static TrainingContentService Service(ILlmClient client, GenerationOptions? limits = null) => new(client, Options.Create(new LlmOptions()), Options.Create(limits ?? new()), NullLogger<TrainingContentService>.Instance);

    [Theory]
    [InlineData("Employee’s  data—access", "EMPLOYEE'S data-access")]
    [InlineData("A \n quoted “fact”", "a quoted \"fact\"")]
    [InlineData("ＡＢＣ", "abc")]
    public void NormalizationIsSymmetric(string a, string b) => Assert.Equal(ExcerptVerifier.Normalize(a), ExcerptVerifier.Normalize(b));
    [Theory]
    [InlineData("", false)] [InlineData("unrelated", false)] [InlineData("must FOLLOW", true)]
    public void ExcerptsMatchOnlySource(string excerpt, bool expected) => Assert.Equal(expected, ExcerptVerifier.IsVerified(excerpt, ExcerptVerifier.Normalize(Transcript)));
    [Fact] public void ValidOutputPasses() => Assert.Empty(TrainingContentValidator.Validate(Valid(), new()));
    [Theory]
    [InlineData("count")] [InlineData("options")] [InlineData("duplicate")] [InlineData("index")] [InlineData("negative")]
    [InlineData("prompt")] [InlineData("explanation")] [InlineData("excerpt")] [InlineData("longExcerpt")] [InlineData("points")] [InlineData("outcome")] [InlineData("duplicatePrompt")] [InlineData("nullQuestion")]
    public void InvalidOutputRejected(string kind)
    {
        var m = Valid(); var q = m.Questions![0];
        m = kind switch { "count" => m with { Questions = [] }, "points" => m with { Summary = new("Overview", ["One", "Two"]) }, "outcome" => m with { Outcome = "insufficient_content" }, _ => m };
        if (m.Questions!.Length > 0) m.Questions[0] = kind switch {
            "options" => q with { Options = ["a", "b", "c"] }, "duplicate" => q with { Options = [" a ", "A", "c", "d"] },
            "index" => q with { CorrectAnswerIndex = 4 }, "negative" => q with { CorrectAnswerIndex = -1 }, "prompt" => q with { Prompt = "" },
            "explanation" => q with { Explanation = "" }, "excerpt" => q with { SourceExcerpt = "" }, "longExcerpt" => q with { SourceExcerpt = string.Join(" ", Enumerable.Repeat("word", 41)) },
            "duplicatePrompt" => q with { Prompt = m.Questions[1].Prompt }, "nullQuestion" => null!, _ => q };
        Assert.NotEmpty(TrainingContentValidator.Validate(m, new()));
    }
    [Fact] public async Task SuccessAndMissingUsage()
    {
        var fake = new ScriptedLlmClient(Response()); var result = await Service(fake).GenerateAsync(Transcript, default);
        Assert.Null(result.ErrorCode); Assert.Null(result.Metadata.InputTokens); Assert.Equal(1, fake.Calls);
        Assert.All(result.Content!.Questions, q => Assert.True(q.ExcerptVerified));
    }
    [Fact] public async Task OneRepairAggregatesUsage()
    {
        var fake = new ScriptedLlmClient(new LlmResponse("{", InputTokens: 2, OutputTokens: 3), Response(tokens: 5));
        var result = await Service(fake).GenerateAsync(Transcript, default);
        Assert.Null(result.ErrorCode); Assert.True(result.Metadata.RepairAttempted); Assert.Equal(2, fake.Calls);
        Assert.Equal(7, result.Metadata.InputTokens); Assert.Equal(8, result.Metadata.OutputTokens);
        Assert.Contains(fake.Requests[1].Messages, m => m.Role == "model");
    }
    [Fact] public async Task TwoInvalidResponsesFail()
    {
        var fake = new ScriptedLlmClient(new LlmResponse("{}"), new LlmResponse("not json"));
        Assert.Equal("provider_invalid_output", (await Service(fake).GenerateAsync(Transcript, default)).ErrorCode); Assert.Equal(2, fake.Calls);
    }
    [Theory]
    [InlineData(FinishReason.MaxTokens, "provider_truncated")]
    [InlineData(FinishReason.Safety, "provider_refused")]
    [InlineData(FinishReason.Other, "provider_invalid_output")]
    public async Task NonRepairableFinish(FinishReason finish, string code)
    {
        var fake = new ScriptedLlmClient(new LlmResponse("{}", finish)); Assert.Equal(code, (await Service(fake).GenerateAsync(Transcript, default)).ErrorCode); Assert.Equal(1, fake.Calls);
    }
    [Fact] public async Task SafetyBlockAndEmptyTextDoNotRepair()
    {
        foreach (var response in new[] { new LlmResponse("{}", BlockReason: "SAFETY"), new LlmResponse(null) })
        { var fake = new ScriptedLlmClient(response); Assert.NotNull((await Service(fake).GenerateAsync(Transcript, default)).ErrorCode); Assert.Equal(1, fake.Calls); }
    }
    [Fact] public async Task InsufficientDoesNotRepair()
    {
        var fake = new ScriptedLlmClient(Response(new("insufficient_content", "few facts", null, [])));
        Assert.Equal("insufficient_content", (await Service(fake).GenerateAsync(Transcript, default)).ErrorCode); Assert.Equal(1, fake.Calls);
    }
    [Fact] public async Task OversizedTokenCountPreventsGeneration()
    {
        var fake = new ScriptedLlmClient { Tokens = 100001 }; Assert.Equal("input_too_large", (await Service(fake).GenerateAsync(Transcript, default)).ErrorCode); Assert.Equal(0, fake.Calls);
    }
    [Fact] public async Task UnknownCountUsesConservativeFallback()
    {
        var fake = new ScriptedLlmClient(Response()) { Tokens = null }; Assert.Null((await Service(fake).GenerateAsync(Transcript, default)).ErrorCode);
        Assert.Equal("input_too_large", (await Service(new ScriptedLlmClient { Tokens = null }, new() { MaxInputTokens = 100 }).GenerateAsync(Transcript, default)).ErrorCode);
    }
    [Fact] public async Task UnverifiedExcerptIsFlaggedNotFailed()
    {
        var m = Valid(); m.Questions![0] = m.Questions[0] with { SourceExcerpt = "not in the source" };
        var result = await Service(new ScriptedLlmClient(Response(m))).GenerateAsync(Transcript, default);
        Assert.Null(result.ErrorCode); Assert.False(result.Content!.Questions[0].ExcerptVerified);
    }
    [Theory]
    [InlineData(LlmErrorKind.RateLimited, "provider_rate_limited")] [InlineData(LlmErrorKind.AuthFailed, "provider_auth_failed")]
    [InlineData(LlmErrorKind.Unavailable, "provider_unavailable")] [InlineData(LlmErrorKind.Timeout, "provider_timeout")] [InlineData(LlmErrorKind.BadRequest, "internal_error")]
    public async Task MapsProviderErrors(LlmErrorKind kind, string code) => Assert.Equal(code, (await Service(new ScriptedLlmClient(new LlmException(kind))).GenerateAsync(Transcript, default)).ErrorCode);
    [Fact] public async Task MockUsesVerbatimExcerpts()
    {
        var result = await Service(new MockLlmClient()).GenerateAsync(Transcript, default);
        Assert.Null(result.ErrorCode); Assert.All(result.Content!.Questions, q => Assert.True(q.ExcerptVerified));
    }
}
