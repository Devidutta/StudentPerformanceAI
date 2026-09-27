using System.Text.Json;
using StudentPerformanceAI.Models;

namespace StudentPerformanceAI.Tools;

/// <summary>Consistent, structured error payloads so the LLM reports "not found" cases clearly instead of guessing.</summary>
internal static class ToolResponses
{
    public static string StudentNotFound(string requestedName, IReadOnlyList<Student> knownStudents) =>
        JsonSerializer.Serialize(new
        {
            Found = false,
            Message = $"No student named '{requestedName}' was found in the student records.",
            KnownStudents = knownStudents.Select(s => s.Name)
        });

    public static string UnknownSubject(string requestedSubject, IReadOnlyList<string> knownSubjects) =>
        JsonSerializer.Serialize(new
        {
            Found = false,
            Message = $"'{requestedSubject}' is not a recognized subject.",
            KnownSubjects = knownSubjects
        });
}
