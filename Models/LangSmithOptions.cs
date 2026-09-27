namespace StudentPerformanceAI.Models;

public sealed class LangSmithOptions
{
    public string? ApiKey { get; init; }
    public required string Project { get; init; }
    public required string BaseUrl { get; init; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ApiKey);
}
