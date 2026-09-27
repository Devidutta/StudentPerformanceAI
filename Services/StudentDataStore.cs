using StudentPerformanceAI.Models;

namespace StudentPerformanceAI.Services;

/// <summary>In-memory view over the freshly-loaded workbook, with case-insensitive lookups for tools.</summary>
public sealed class StudentDataStore
{
    private readonly Dictionary<string, Student> _byName;
    private readonly Dictionary<string, string> _subjectByLowerName;

    public IReadOnlyList<Student> Students { get; }
    public IReadOnlyList<string> Subjects { get; }

    public StudentDataStore(string workbookPath)
    {
        var (students, subjects) = ExcelStudentService.LoadStudents(workbookPath);
        Students = students;
        Subjects = subjects;
        _byName = students.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        _subjectByLowerName = subjects.ToDictionary(s => s, s => s, StringComparer.OrdinalIgnoreCase);
    }

    public Student? FindStudent(string name) =>
        _byName.TryGetValue(name.Trim(), out var student) ? student : null;

    /// <summary>Returns the canonical subject name (as it appears in the workbook) or null if unknown.</summary>
    public string? ResolveSubject(string subject) =>
        _subjectByLowerName.TryGetValue(subject.Trim(), out var canonical) ? canonical : null;
}
