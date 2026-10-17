using System.Collections.Concurrent;
using System.Text.Json;
using LearnSphere.Api.Generation;

namespace LearnSphere.Api.Jobs;

// A result-only checkpoint, on the same mounted App_Data volume. No transcript or API key is stored.
// In-memory retention additionally permits retry when the filesystem itself is temporarily unwritable.
public sealed class ResultJournal
{
    private readonly string directory;
    private readonly ConcurrentDictionary<Guid, GenerationResult> cache = new();
    private readonly ConcurrentDictionary<Guid, byte> failed = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public ResultJournal(IConfiguration configuration, IHostEnvironment environment)
    {
        directory = configuration["Recovery:Directory"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "result-recovery");
        Directory.CreateDirectory(directory);
    }
    public async Task StageAsync(Guid id, GenerationResult result, CancellationToken ct)
    {
        cache[id] = result;
        string path = FilePath(id); string temp = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(result, Json), ct);
            File.Move(temp, path, true);
        }
        catch { failed[id] = 0; throw; }
    }
    public GenerationResult? Read(Guid id)
    {
        if (cache.TryGetValue(id, out var result)) return result;
        if (!File.Exists(FilePath(id))) return null;
        var read = JsonSerializer.Deserialize<GenerationResult>(File.ReadAllText(FilePath(id)), Json);
        if (read is not null) cache[id] = read;
        return read;
    }
    public void MarkFailed(Guid id) => failed[id] = 0;
    public bool IsFailed(Guid id) => failed.ContainsKey(id);
    public void Complete(Guid id)
    {
        // Deletion failure must never change a successfully committed result into a failed job.
        try { File.Delete(FilePath(id)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        cache.TryRemove(id, out _); failed.TryRemove(id, out _);
    }
    private string FilePath(Guid id) => Path.Combine(directory, id.ToString("N") + ".json");
}
