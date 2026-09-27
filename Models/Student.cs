namespace StudentPerformanceAI.Models;

public sealed class Student
{
    public required string Name { get; init; }

    /// <summary>Subject name -> score. Keys are the canonical subject names read from the workbook header row.</summary>
    public required IReadOnlyDictionary<string, double> Scores { get; init; }

    public double Average => Scores.Count == 0 ? 0 : Scores.Values.Average();
}
