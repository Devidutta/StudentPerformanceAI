using System.ComponentModel;
using ModelContextProtocol.Server;
using StudentPerformanceAI.Tools;

namespace StudentPerformanceAI.McpServer.McpTools;

/// <summary>
/// MCP-facing wrapper around the capstone project's deterministic <see cref="StudentScoreTools"/>. All
/// logic lives in the referenced StudentPerformanceAI project; this class only adds MCP tool metadata
/// so the same calculations are reachable over the Model Context Protocol.
/// </summary>
[McpServerToolType]
public sealed class StudentScoreMcpTools(StudentScoreTools tools)
{
    [McpServerTool, Description("Returns the subject-wise scores and average for a single student, given their name (case-insensitive).")]
    public string GetStudentScores(
        [Description("The student's name, e.g. 'Mary'.")] string studentName) =>
        tools.GetStudentScores(studentName);

    [McpServerTool, Description("Calculates a single student's average score across all subjects, given their name (case-insensitive).")]
    public string GetStudentAverage(
        [Description("The student's name, e.g. 'Mary'.")] string studentName) =>
        tools.GetStudentAverage(studentName);

    [McpServerTool, Description("Finds the student with the highest score in a given subject.")]
    public string GetHighestScorerBySubject(
        [Description("Subject name as it appears in the workbook, e.g. 'Mathematics'.")] string subject) =>
        tools.GetHighestScorerBySubject(subject);

    [McpServerTool, Description("Determines the overall top-performing student, based on average score across all subjects.")]
    public string GetOverallTopStudent() => tools.GetOverallTopStudent();

    [McpServerTool, Description("Lists every student who scored above a given threshold in a specific subject, ordered highest first.")]
    public string GetStudentsAboveScore(
        [Description("Subject name as it appears in the workbook, e.g. 'Science'.")] string subject,
        [Description("Score threshold; students strictly above this score are returned.")] double threshold) =>
        tools.GetStudentsAboveScore(subject, threshold);

    [McpServerTool, Description("Calculates the class average score for a specific subject.")]
    public string GetClassAverageBySubject(
        [Description("Subject name as it appears in the workbook, e.g. 'English'.")] string subject) =>
        tools.GetClassAverageBySubject(subject);
}
