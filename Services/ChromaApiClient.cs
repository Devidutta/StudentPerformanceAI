using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StudentPerformanceAI.Models;

namespace StudentPerformanceAI.Services;

/// <summary>Thrown when Chroma's v2 API returns a non-success response.</summary>
public sealed class ChromaApiException(int statusCode, string body)
    : Exception($"HTTP {statusCode} - {body}")
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Minimal client for Chroma's v2 REST API (https://docs.trychroma.com), used directly over HttpClient
/// because the only stable community .NET package for Chroma (ChromaDB.Client) only speaks the legacy
/// v1 API, which Chroma Cloud does not serve at all (confirmed live: /api/v1/heartbeat returns 410
/// "The v1 API is deprecated. Please use /v2 apis"). This works against both Chroma Cloud and a
/// modern self-hosted server.
/// </summary>
public sealed class ChromaApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ChromaOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string? _tenant;
    private string? _database;
    private string? _collectionId;

    public ChromaApiClient(ChromaOptions options)
    {
        _options = options;
        _http = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
        if (!string.IsNullOrWhiteSpace(options.AuthToken))
        {
            _http.DefaultRequestHeaders.Add("x-chroma-token", options.AuthToken);
        }
    }

    /// <summary>Resolves tenant/database (explicit config, or via Chroma Cloud's identity lookup) and gets/creates the collection.</summary>
    public async Task InitializeAsync(string collectionName, CancellationToken cancellationToken)
    {
        (_tenant, _database) = await ResolveTenantAndDatabaseAsync(cancellationToken);

        var response = await PostAsync<CollectionResponse>(
            $"api/v2/tenants/{_tenant}/databases/{_database}/collections",
            new { name = collectionName, get_or_create = true, metadata = new Dictionary<string, object> { ["hnsw:space"] = "cosine" } },
            cancellationToken);
        _collectionId = response.Id;
    }

    private async Task<(string Tenant, string Database)> ResolveTenantAndDatabaseAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.Tenant) && !string.IsNullOrWhiteSpace(_options.Database))
        {
            return (_options.Tenant, _options.Database);
        }

        try
        {
            var identity = await GetAsync<IdentityResponse>("api/v2/auth/identity", cancellationToken);
            var database = identity.Databases.Contains("default_database")
                ? "default_database"
                : identity.Databases.FirstOrDefault() ?? "default_database";
            return (_options.Tenant ?? identity.Tenant, _options.Database ?? database);
        }
        catch
        {
            // Self-hosted servers without auth configured may not implement /auth/identity - fall back
            // to Chroma's long-standing defaults.
            return (_options.Tenant ?? "default_tenant", _options.Database ?? "default_database");
        }
    }

    public async Task<Dictionary<string, string?>> GetDocumentsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var response = await PostAsync<GetResponse>(
            CollectionPath("get"),
            new { ids, include = new[] { "documents" } },
            cancellationToken);

        var result = new Dictionary<string, string?>();
        for (var i = 0; i < response.Ids.Length; i++)
        {
            result[response.Ids[i]] = response.Documents?[i];
        }

        return result;
    }

    public Task UpsertAsync(
        IReadOnlyList<string> ids,
        IReadOnlyList<float[]> embeddings,
        IReadOnlyList<Dictionary<string, object>> metadatas,
        IReadOnlyList<string> documents,
        CancellationToken cancellationToken) =>
        PostAsync<object>(CollectionPath("upsert"), new { ids, embeddings, metadatas, documents }, cancellationToken);

    public async Task<IReadOnlyList<(string? StudentName, string? Section, string? Document, float Distance)>> QueryAsync(
        float[] queryEmbedding, int nResults, CancellationToken cancellationToken)
    {
        var response = await PostAsync<QueryResponse>(
            CollectionPath("query"),
            new
            {
                query_embeddings = new[] { queryEmbedding },
                n_results = nResults,
                include = new[] { "metadatas", "documents", "distances" }
            },
            cancellationToken);

        var count = response.Ids.Length == 0 ? 0 : response.Ids[0].Length;
        var results = new List<(string?, string?, string?, float)>(count);
        for (var i = 0; i < count; i++)
        {
            var metadata = response.Metadatas?[0][i];
            results.Add((
                metadata?.GetValueOrDefault("studentName")?.ToString(),
                metadata?.GetValueOrDefault("section")?.ToString(),
                response.Documents?[0][i],
                response.Distances?[0][i] ?? 1f));
        }

        return results;
    }

    private string CollectionPath(string operation) =>
        $"api/v2/tenants/{_tenant}/databases/{_database}/collections/{_collectionId}/{operation}";

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync(path, cancellationToken);
        return await DeserializeOrThrowAsync<T>(response);
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        return await DeserializeOrThrowAsync<T>(response);
    }

    private static async Task<T> DeserializeOrThrowAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new ChromaApiException((int)response.StatusCode, body);
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    public void Dispose() => _http.Dispose();

    private sealed record IdentityResponse(string Tenant, string[] Databases);

    private sealed record CollectionResponse(string Id);

    private sealed record GetResponse(string[] Ids, string?[]? Documents);

    private sealed record QueryResponse(
        string[][] Ids,
        string?[][]? Documents,
        float?[][]? Distances,
        [property: JsonPropertyName("metadatas")] Dictionary<string, object>?[][]? Metadatas);
}
