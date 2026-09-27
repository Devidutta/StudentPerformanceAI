namespace StudentPerformanceAI.Models;

public sealed class OpenAIOptions
{
    public required string ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public required string Model { get; init; }
    public required string EmbeddingModel { get; init; }
}
