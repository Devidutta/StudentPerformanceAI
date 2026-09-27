using System.Text.Json;

namespace StudentPerformanceAI.Services;

/// <summary>
/// Caches chunk embeddings (keyed by a hash of the chunk text) in a local JSON file so restarting the
/// app doesn't re-embed unchanged teacher notes on every run. This is the only persistence used by the
/// knowledge agent - there is no external vector database.
/// </summary>
public sealed class EmbeddingCacheService(string cacheFilePath)
{
    private Dictionary<string, float[]> _cache = new();

    public void Load()
    {
        if (!File.Exists(cacheFilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(cacheFilePath);
            _cache = JsonSerializer.Deserialize<Dictionary<string, float[]>>(json) ?? new Dictionary<string, float[]>();
        }
        catch (JsonException)
        {
            _cache = new Dictionary<string, float[]>();
        }
    }

    public float[]? TryGet(string chunkHash) =>
        _cache.TryGetValue(chunkHash, out var embedding) ? embedding : null;

    public void Set(string chunkHash, float[] embedding) => _cache[chunkHash] = embedding;

    public void Save()
    {
        var directory = Path.GetDirectoryName(cacheFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(cacheFilePath, JsonSerializer.Serialize(_cache));
    }
}
