using System.ComponentModel;
using System.Text.Json;
using StudentPerformanceAI.Services;

namespace StudentPerformanceAI.Tools;

/// <summary>
/// Deterministic C# tools backing the Student Scores Agent. Every calculation here is plain C# over the
/// data loaded from the workbook - the language model never computes or invents a mark.
/// </summary>
public sealed class StudentScoreTools(StudentDataStore data)
{
    [Description("Returns the subject-wise scores and average for a single student, given their name (case-insensitive).")]
    public string GetStudentScores(
        [Description("The student's name, e.g. 'Mary'.")] string studentName)
    {
        var student = data.FindStudent(studentName);
        if (student is null)
        {
            return ToolResponses.StudentNotFound(studentName, data.Students);
        }

        return JsonSerializer.Serialize(new
        {
            Found = true,
            student.Name,
            student.Scores,
            Average = Math.Round(student.Average, 2)
        });
    }

    [Description("Calculates a single student's average score across all subjects, given their name (case-insensitive).")]
    public string GetStudentAverage(
        [Description("The student's name, e.g. 'Mary'.")] string studentName)
    {
        var student = data.FindStudent(studentName);
        if (student is null)
        {
            return ToolResponses.StudentNotFound(studentName, data.Students);
        }

        return JsonSerializer.Serialize(new { Found = true, student.Name, Average = Math.Round(student.Average, 2) });
    }

    [Description("Finds the student with the highest score in a given subject.")]
    public string GetHighestScorerBySubject(
        [Description("Subject name as it appears in the workbook, e.g. 'Mathematics'.")] string subject)
    {
        var canonicalSubject = data.ResolveSubject(subject);
        if (canonicalSubject is null)
        {
            return ToolResponses.UnknownSubject(subject, data.Subjects);
        }

        var top = data.Students.OrderByDescending(s => s.Scores[canonicalSubject]).First();
        return JsonSerializer.Serialize(new
        {
            Found = true,
            Subject = canonicalSubject,
            top.Name,
            Score = top.Scores[canonicalSubject]
        });
    }

    [Description("Determines the overall top-performing student, based on average score across all subjects.")]
    public string GetOverallTopStudent()
    {
        var top = data.Students.OrderByDescending(s => s.Average).First();
        return JsonSerializer.Serialize(new { Found = true, top.Name, Average = Math.Round(top.Average, 2) });
    }

    [Description("Lists every student who scored above a given threshold in a specific subject, ordered highest first.")]
    public string GetStudentsAboveScore(
        [Description("Subject name as it appears in the workbook, e.g. 'Science'.")] string subject,
        [Description("Score threshold; students strictly above this score are returned.")] double threshold)
    {
        var canonicalSubject = data.ResolveSubject(subject);
        if (canonicalSubject is null)
        {
            return ToolResponses.UnknownSubject(subject, data.Subjects);
        }

        var matches = data.Students
            .Where(s => s.Scores[canonicalSubject] > threshold)
            .OrderByDescending(s => s.Scores[canonicalSubject])
            .Select(s => new { s.Name, Score = s.Scores[canonicalSubject] })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            Found = true,
            Subject = canonicalSubject,
            Threshold = threshold,
            Students = matches
        });
    }

    [Description("Calculates the class average score for a specific subject.")]
    public string GetClassAverageBySubject(
        [Description("Subject name as it appears in the workbook, e.g. 'English'.")] string subject)
    {
        var canonicalSubject = data.ResolveSubject(subject);
        if (canonicalSubject is null)
        {
            return ToolResponses.UnknownSubject(subject, data.Subjects);
        }

        var average = data.Students.Average(s => s.Scores[canonicalSubject]);
        return JsonSerializer.Serialize(new
        {
            Found = true,
            Subject = canonicalSubject,
            ClassAverage = Math.Round(average, 2)
        });
    }
}
