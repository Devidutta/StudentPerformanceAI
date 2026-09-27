namespace StudentPerformanceAI.Models;

public sealed class ChromaOptions
{
    public required string BaseUrl { get; init; }
    public required string CollectionName { get; init; }
    public string? AuthToken { get; init; }
    public string? Tenant { get; init; }
    public string? Database { get; init; }
}
