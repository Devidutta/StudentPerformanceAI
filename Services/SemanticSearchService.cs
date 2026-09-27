using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ChromaDB.Client;
using OpenAI.Embeddings;
using StudentPerformanceAI.Models;

namespace StudentPerformanceAI.Services;

/// <summary>
/// Semantic search over the teacher-observation notes, backed by a Chroma vector database: chunks the
/// notes file, embeds each chunk with the OpenAI embeddings API, upserts it into a Chroma collection,
/// and answers queries via Chroma's nearest-neighbor search. Chroma is the only persistence layer -
/// there is no local cache file.
/// </summary>
public sealed partial class SemanticSearchService : IDisposable
{
    private const double RelevanceThreshold = 0.24;

    private readonly EmbeddingClient _embeddingClient;
    private readonly TracingService _tracing;
    private readonly HttpClient _chromaHttpClient;
    private readonly ChromaConfigurationOptions _chromaConfig;
    private readonly string _collectionName;
    private ChromaCollectionClient? _collectionClient;

    public SemanticSearchService(
        EmbeddingClient embeddingClient, ChromaOptions chromaOptions, TracingService tracing)
    {
        _embeddingClient = embeddingClient;
        _tracing = tracing;
        _collectionName = chromaOptions.CollectionName;
        _chromaConfig = new ChromaConfigurationOptions(
            uri: chromaOptions.BaseUrl,
            defaultTenant: chromaOptions.Tenant,
            defaultDatabase: chromaOptions.Database,
            chromaToken: chromaOptions.AuthToken);
        _chromaHttpClient = new HttpClient();
    }

    public async Task InitializeAsync(string notesFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(notesFilePath))
        {
            throw new FileNotFoundException($"Teacher observations file not found at '{notesFilePath}'.", notesFilePath);
        }

        var chromaClient = new ChromaClient(_chromaConfig, _chromaHttpClient);
        var collection = await chromaClient.GetOrCreateCollection(
            _collectionName,
            metadata: new Dictionary<string, object> { ["hnsw:space"] = "cosine" });
        _collectionClient = new ChromaCollectionClient(collection, _chromaConfig, _chromaHttpClient);

        var chunks = ChunkNotes(await File.ReadAllTextAsync(notesFilePath, cancellationToken));
        var chunkIds = chunks.Select(c => ComputeId(c.StudentName, c.Section)).ToList();

        var existing = await _collectionClient.Get(chunkIds, include: ChromaGetInclude.Documents);
        var existingTextById = existing.ToDictionary(e => e.Id, e => e.Document);

        var idsToUpsert = new List<string>();
        var embeddingsToUpsert = new List<ReadOnlyMemory<float>>();
        var metadatasToUpsert = new List<Dictionary<string, object>>();
        var documentsToUpsert = new List<string>();

        foreach (var chunk in chunks)
        {
            var id = ComputeId(chunk.StudentName, chunk.Section);
            if (existingTextById.TryGetValue(id, out var existingText) && existingText == chunk.Text)
            {
                continue; // unchanged - skip re-embedding
            }

            await using var span = _tracing.StartSpan(
                "embed_chunk", "embedding", new { chunk.StudentName, chunk.Section });
            var result = await _embeddingClient.GenerateEmbeddingAsync(chunk.Text, cancellationToken: cancellationToken);
            var embedding = result.Value.ToFloats();
            await span.EndAsync(new { Dimensions = embedding.Length });

            idsToUpsert.Add(id);
            embeddingsToUpsert.Add(embedding);
            metadatasToUpsert.Add(new Dictionary<string, object>
            {
                ["studentName"] = chunk.StudentName,
                ["section"] = chunk.Section
            });
            documentsToUpsert.Add(chunk.Text);
        }

        if (idsToUpsert.Count > 0)
        {
            await _collectionClient.Upsert(idsToUpsert, embeddingsToUpsert, metadatasToUpsert, documentsToUpsert);
        }
    }

    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        string query, int topK = 3, CancellationToken cancellationToken = default)
    {
        if (_collectionClient is null)
        {
            throw new InvalidOperationException("SemanticSearchService.InitializeAsync must be called before SearchAsync.");
        }

        var queryResult = await _embeddingClient.GenerateEmbeddingAsync(query, cancellationToken: cancellationToken);
        var matches = await _collectionClient.Query(
            queryResult.Value.ToFloats(),
            nResults: topK,
            include: ChromaQueryInclude.Metadatas | ChromaQueryInclude.Documents | ChromaQueryInclude.Distances);

        return matches
            .Select(m => new SemanticSearchResult(
                StudentName: m.Metadata?.GetValueOrDefault("studentName")?.ToString() ?? "Unknown",
                Section: m.Metadata?.GetValueOrDefault("section")?.ToString() ?? "Unknown",
                Text: m.Document ?? string.Empty,
                Score: 1 - m.Distance))
            .Where(r => r.Score >= RelevanceThreshold)
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    private static string ComputeId(string studentName, string section) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{studentName}|{section}")));

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

    public void Dispose() => _chromaHttpClient.Dispose();

    [GeneratedRegex(@"(?m)^###\s+")]
    private static partial Regex StudentHeaderRegex();

    [GeneratedRegex(@"(?m)^(Strengths|Development Areas|Recommended Actions):\s*(.+)$")]
    private static partial Regex SectionRegex();

    private readonly record struct NoteChunk(string StudentName, string Section, string Text);
}
