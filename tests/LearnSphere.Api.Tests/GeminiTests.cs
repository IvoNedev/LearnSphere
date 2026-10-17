using System.Net;
using System.Text;
using System.Text.Json;
using LearnSphere.Api.Configuration;
using LearnSphere.Api.Generation;
using LearnSphere.Api.Providers;
using Microsoft.Extensions.Options;

namespace LearnSphere.Api.Tests;

public sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
}
public class GeminiTests
{
    private static GeminiLlmClient Client(HttpMessageHandler handler, double timeout = 2) => new(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeout) }, Options.Create(new LlmOptions { Provider = "Gemini", Model = "test-model", ApiKey = "fake-test-credential" }));
    [Fact] public async Task WireContractAndThoughtFiltering()
    {
        var client = Client(new FakeHandler(async (request, ct) =>
        {
            Assert.EndsWith("test-model:generateContent", request.RequestUri!.ToString());
            Assert.Equal("fake-test-credential", request.Headers.GetValues("x-goog-api-key").Single());
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var root = doc.RootElement;
            Assert.True(root.TryGetProperty("systemInstruction", out _)); Assert.Equal("user", root.GetProperty("contents")[0].GetProperty("role").GetString());
            var config = root.GetProperty("generationConfig"); Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString()); Assert.True(config.TryGetProperty("responseJsonSchema", out _)); Assert.Equal(8192, config.GetProperty("maxOutputTokens").GetInt32());
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"candidates":[{"finishReason":"STOP","content":{"parts":[{"thought":true,"text":"hidden"},{"text":"visible"}]}}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":20,"thoughtsTokenCount":3}}""") };
        }));
        var response = await client.GenerateAsync(Prompts.Create("source"), default); Assert.Equal("visible", response.Text); Assert.Equal(10, response.InputTokens); Assert.Equal(20, response.OutputTokens); Assert.Equal(3, response.ThoughtTokens);
    }
    [Theory] [InlineData("MAX_TOKENS", FinishReason.MaxTokens)] [InlineData("SAFETY", FinishReason.Safety)] [InlineData("STOP", FinishReason.Stop)]
    public async Task FinishReasons(string finish, FinishReason expected)
    {
        var client = Client(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"candidates\":[{{\"finishReason\":\"{finish}\",\"content\":{{\"parts\":[]}}}}]}}") })));
        var response = await client.GenerateAsync(Prompts.Create("source"), default); Assert.Equal(expected, response.FinishReason); Assert.Null(response.InputTokens);
    }
    [Theory] [InlineData(429, LlmErrorKind.RateLimited)] [InlineData(401, LlmErrorKind.AuthFailed)] [InlineData(403, LlmErrorKind.AuthFailed)] [InlineData(503, LlmErrorKind.Unavailable)] [InlineData(400, LlmErrorKind.BadRequest)]
    public async Task ErrorsDoNotLeakBodiesOrRetry(int status, LlmErrorKind expected)
    {
        int calls = 0;
        var client = Client(new FakeHandler((_, _) => { calls++; var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("PRIVATE PROVIDER BODY") }; response.Headers.RetryAfter = new(TimeSpan.FromSeconds(10)); return Task.FromResult(response); }));
        var exception = await Assert.ThrowsAsync<LlmException>(() => client.GenerateAsync(Prompts.Create("source"), default));
        Assert.Equal(expected, exception.Kind); Assert.DoesNotContain("PRIVATE", exception.ToString()); Assert.Equal(1, calls); Assert.Equal(TimeSpan.FromSeconds(10), exception.RetryAfter);
    }
    [Fact] public async Task CountTokensUsesFullRequestAndFailureIsUnknown()
    {
        var client = Client(new FakeHandler(async (request, ct) => {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.True(doc.RootElement.GetProperty("generateContentRequest").TryGetProperty("systemInstruction", out _));
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"totalTokens\":123}") };
        }));
        Assert.Equal(123, await client.CountTokensAsync(Prompts.Create("source"), default));
        Assert.Null(await Client(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))).CountTokensAsync(Prompts.Create("source"), default));
    }
    [Fact] public async Task HttpTimeoutIsMapped()
    {
        var client = Client(new FakeHandler(async (_, ct) => { await Task.Delay(1000, ct); return new(HttpStatusCode.OK); }), .02);
        var exception = await Assert.ThrowsAsync<LlmException>(() => client.GenerateAsync(Prompts.Create("source"), default)); Assert.Equal(LlmErrorKind.Timeout, exception.Kind);
    }
}
