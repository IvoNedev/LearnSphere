using System.Text.Json;
using System.Text.RegularExpressions;
using LearnSphere.Api.Domain;
using LearnSphere.Api.Generation;

namespace LearnSphere.Api.Providers;

public sealed class MockLlmClient : ILlmClient
{
    public Task<int?> CountTokensAsync(LlmRequest request, CancellationToken ct) => Task.FromResult<int?>((request.Messages.Sum(m => m.Text.Length) + request.SystemPrompt.Length) / 4);
    public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var transcript = request.Messages[0].Text.Replace("<transcript>\n", "").Replace("\n</transcript>", "");
        var facts = Regex.Split(transcript, @"(?<=[.!?])\s+|\r?\n\s*\r?\n")
            .Select(x => x.Trim()).Where(x => x.Length > 25 && Regex.Matches(x, @"\S+").Count <= 40)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (request.JsonSchema.GetProperty("properties").TryGetProperty("facts", out _))
            return Task.FromResult(new LlmResponse(JsonSerializer.Serialize(new SectionNotes(string.Join(" ", facts.Take(2)), facts.Take(10).Select(f => new SectionFact(f, f)).ToArray()), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        if (transcript.StartsWith("Source section notes", StringComparison.Ordinal))
        {
            var jsonStart = transcript.IndexOf('[');
            var noteText = transcript.Substring(jsonStart).Replace("\n</transcript>", "");
            var notes = JsonSerializer.Deserialize<SectionNotes[]>(noteText, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            facts = notes.SelectMany(n => n.Facts).Select(f => f.SourceExcerpt).Distinct().ToArray();
        }
        ModelOutput content;
        if (facts.Length < 5) content = new("insufficient_content", "Not enough distinct short factual statements.", null, []);
        else
        {
            var selected = Enumerable.Range(0, 5).Select(i => facts[i * (facts.Length - 1) / 4]).ToArray();
            content = new("ok", "", new(string.Join(" ", facts.Take(2)), selected), selected.Select((fact, i) =>
            {
                int correct = i % 4;
                string[] alternatives = ["Only optional personal preferences apply.", "The course provides no guidance on this topic.", "The procedure should always be skipped."];
                var choices = alternatives.ToList(); choices.Insert(correct, fact);
                return new QuizQuestion($"Which statement matches source point {i + 1}?", choices.ToArray(), correct, "The source states: " + fact, fact);
            }).ToArray());
        }
        return Task.FromResult(new LlmResponse(JsonSerializer.Serialize(content, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }
}
