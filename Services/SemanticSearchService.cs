using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenAI.Embeddings;
using StudentPerformanceAI.Models;

namespace StudentPerformanceAI.Services;

/// <summary>
/// In-process semantic search over the teacher-observation notes: chunks the notes file, embeds each
/// chunk with the OpenAI embeddings API, and answers queries via cosine-similarity search. No external
/// vector database or search service is used.
/// </summary>
public sealed partial class SemanticSearchService
{
    private const double RelevanceThreshold = 0.24;

    private readonly EmbeddingClient _embeddingClient;
    private readonly EmbeddingCacheService _cache;
    private readonly TracingService _tracing;
    private readonly List<IndexedChunk> _index = [];

    public SemanticSearchService(EmbeddingClient embeddingClient, EmbeddingCacheService cache, TracingService tracing)
    {
        _embeddingClient = embeddingClient;
        _cache = cache;
        _tracing = tracing;
    }

    public async Task InitializeAsync(string notesFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(notesFilePath))
        {
            throw new FileNotFoundException($"Teacher observations file not found at '{notesFilePath}'.", notesFilePath);
        }

        _cache.Load();
        _index.Clear();

        var chunks = ChunkNotes(await File.ReadAllTextAsync(notesFilePath, cancellationToken));
        foreach (var chunk in chunks)
        {
            var hash = ComputeHash(chunk.Text);
            var embedding = _cache.TryGet(hash);
            if (embedding is null)
            {
                await using var span = _tracing.StartSpan(
                    "embed_chunk", "embedding", new { chunk.StudentName, chunk.Section });
                var result = await _embeddingClient.GenerateEmbeddingAsync(chunk.Text, cancellationToken: cancellationToken);
                embedding = result.Value.ToFloats().ToArray();
                _cache.Set(hash, embedding);
                await span.EndAsync(new { Dimensions = embedding.Length });
            }

            _index.Add(new IndexedChunk(chunk.StudentName, chunk.Section, chunk.Text, embedding));
        }

        _cache.Save();
    }

    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        string query, int topK = 3, CancellationToken cancellationToken = default)
    {
        var queryResult = await _embeddingClient.GenerateEmbeddingAsync(query, cancellationToken: cancellationToken);
        var queryEmbedding = queryResult.Value.ToFloats();

        return _index
            .Select(chunk => new SemanticSearchResult(
                chunk.StudentName, chunk.Section, chunk.Text, CosineSimilarity(queryEmbedding.Span, chunk.Embedding)))
            .Where(r => r.Score >= RelevanceThreshold)
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    private static string ComputeHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    /// <summary>
    /// Splits the notes file into per-student, per-section chunks. Expected format:
    /// "### StudentName" header lines followed by "Strengths:", "Development Areas:" and
    /// "Recommended Actions:" lines. Parsing is purely mechanical - no LLM involved.
    /// </summary>
    private static List<NoteChunk> ChunkNotes(string content)
    {
        var chunks = new List<NoteChunk>();
        var blocks = StudentHeaderRegex().Split(content).Where(b => !string.IsNullOrWhiteSpace(b));

        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            var studentName = lines[0].Trim();
            if (string.IsNullOrWhiteSpace(studentName))
            {
                continue;
            }

            var body = string.Join('\n', lines.Skip(1));
            foreach (Match match in SectionRegex().Matches(body))
            {
                var section = match.Groups[1].Value.Trim();
                var text = match.Groups[2].Value.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                chunks.Add(new NoteChunk(studentName, section, $"{studentName} - {section}: {text}"));
            }
        }

        return chunks;
    }

    [GeneratedRegex(@"(?m)^###\s+")]
    private static partial Regex StudentHeaderRegex();

    [GeneratedRegex(@"(?m)^(Strengths|Development Areas|Recommended Actions):\s*(.+)$")]
    private static partial Regex SectionRegex();

    private readonly record struct NoteChunk(string StudentName, string Section, string Text);

    private readonly record struct IndexedChunk(string StudentName, string Section, string Text, float[] Embedding);
}
