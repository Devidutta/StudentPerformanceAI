using ClosedXML.Excel;
using StudentPerformanceAI.Models;

namespace StudentPerformanceAI.Services;

/// <summary>Reads student marks from the workbook. Subjects are derived from the header row, not hard-coded.</summary>
public static class ExcelStudentService
{
    public static (IReadOnlyList<Student> Students, IReadOnlyList<string> Subjects) LoadStudents(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Student scores workbook not found at '{path}'.", path);
        }

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("StudentScores workbook does not contain any worksheets.");

        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastColumn < 2)
        {
            throw new InvalidDataException(
                "StudentScores workbook must contain a 'Student' column plus at least one subject column.");
        }

        var headerRow = sheet.Row(1);
        var studentHeader = headerRow.Cell(1).GetString().Trim();
        if (!string.Equals(studentHeader, "Student", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The first column of the StudentScores workbook must be named 'Student'.");
        }

        var subjects = new List<string>();
        for (var col = 2; col <= lastColumn; col++)
        {
            var header = headerRow.Cell(col).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(header))
            {
                subjects.Add(header);
            }
        }

        if (subjects.Count == 0)
        {
            throw new InvalidDataException("StudentScores workbook does not define any subject columns.");
        }

        var students = new List<Student>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var name = sheet.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (var col = 2; col <= lastColumn; col++)
            {
                var subject = headerRow.Cell(col).GetString().Trim();
                if (string.IsNullOrWhiteSpace(subject))
                {
                    continue;
                }

                var cell = sheet.Cell(row, col);
                scores[subject] = cell.IsEmpty() ? 0 : cell.GetDouble();
            }

            students.Add(new Student { Name = name, Scores = scores });
        }

        if (students.Count == 0)
        {
            throw new InvalidDataException("StudentScores workbook does not contain any student rows.");
        }

        return (students, subjects);
    }
}
