namespace StudentPerformanceAI.Models;

public sealed class DataPathOptions
{
    public required string ScoresWorkbook { get; init; }
    public required string PerformanceNotes { get; init; }
    public required string EmbeddingCache { get; init; }
}
