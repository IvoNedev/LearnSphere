using System.Text.Json;
using LearnSphere.Api.Configuration;
using LearnSphere.Api.Generation;
using LearnSphere.Api.Providers;

namespace LearnSphere.Api.Tests;

public class ChunkTests
{
    [Fact]
    public void SplitPreservesEveryCharacterAndMakesBoundedProgress()
    {
        string source = string.Concat(Enumerable.Repeat("A paragraph with facts.\n\n" + new string('x', 700) + "😀", 5));
        var chunks = TranscriptSections.Split(source, 500, 20);
        Assert.Equal(source, string.Concat(chunks)); Assert.All(chunks, c => { Assert.InRange(c.Length, 1, 500); Assert.False(char.IsHighSurrogate(c[^1])); });
    }
    [Fact]
    public void ChunkLimitIsExplicit()
    {
        var error = Assert.Throws<GenerationStepException>(() => TranscriptSections.Split(new string('x', 2000), 500, 2)); Assert.Equal("chunk_limit_exceeded", error.Code);
    }
    [Fact]
    public async Task SectionProcessingProducesOnlyFinalGroundedQuiz()
    {
        string source = string.Join("\n", Enumerable.Repeat(GenerationTests.Transcript, 10));
        var recorder = new CountingMock();
        var result = await GenerationTests.Service(recorder, new GenerationOptions { ChunkTargetChars = 2000, MaxChunks = 20 }).GenerateAsync(source, default, true);
        Assert.Null(result.ErrorCode); Assert.True(result.Metadata.ChunkCount > 1); Assert.Equal(result.Metadata.ChunkCount + 1, recorder.Calls);
        Assert.Equal(5, result.Content!.Questions.Length); Assert.All(result.Content.Questions, q => Assert.True(q.ExcerptVerified));
    }
    [Fact]
    public async Task FailedSectionStopsAndPublishesNoPartialContent()
    {
        string source = string.Join("\n", Enumerable.Repeat(GenerationTests.Transcript, 5));
        var firstChunk = TranscriptSections.Split(source, 2000, 20)[0]; var excerpt = firstChunk.Split('.')[0] + ".";
        var note = new SectionNotes("Summary", [new("Fact", excerpt)]);
        var fake = new ScriptedLlmClient(new LlmResponse(JsonSerializer.Serialize(note, new JsonSerializerOptions(JsonSerializerDefaults.Web))), new LlmException(LlmErrorKind.RateLimited));
        var result = await GenerationTests.Service(fake, new GenerationOptions { ChunkTargetChars = 2000 }).GenerateAsync(source, default, true);
        Assert.Equal("provider_rate_limited", result.ErrorCode); Assert.Null(result.Content); Assert.Equal(2, fake.Calls);
    }
    [Fact]
    public async Task InventedSectionQuoteIsRejected()
    {
        var note = new SectionNotes("Summary", [new("Fact", "This was never in the transcript.")]);
        var fake = new ScriptedLlmClient(new LlmResponse(JsonSerializer.Serialize(note, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var result = await GenerationTests.Service(fake, new GenerationOptions { ChunkTargetChars = 500 }).GenerateAsync(GenerationTests.Transcript, default, true);
        Assert.Equal("chunk_invalid_output", result.ErrorCode); Assert.Equal(1, fake.Calls);
    }
    [Fact]
    public async Task MockKeepsSourceSentencesContainingIgnore()
    {
        string source = string.Join(" ", Enumerable.Range(1, 8).Select(i =>
            $"Procedure {i} says employees must not ignore customer privacy safeguards when handling records."));
        var result = await GenerationTests.Service(new MockLlmClient()).GenerateAsync(source, default);

        Assert.Null(result.ErrorCode);
        Assert.Equal(5, result.Content!.Questions.Length);
        Assert.All(result.Content.Questions, question =>
        {
            Assert.Contains("ignore", question.SourceExcerpt);
            Assert.True(question.ExcerptVerified);
        });
    }

    private sealed class CountingMock : ILlmClient
    {
        private readonly MockLlmClient inner = new(); public int Calls { get; private set; }
        public Task<int?> CountTokensAsync(LlmRequest request, CancellationToken ct) => inner.CountTokensAsync(request, ct);
        public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct) { Calls++; return inner.GenerateAsync(request, ct); }
    }
}
