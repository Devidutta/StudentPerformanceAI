using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using StudentPerformanceAI.Tools;

namespace StudentPerformanceAI.Agents;

/// <summary>Specialist agent for structured/numerical questions. Only ever calls deterministic score tools.</summary>
public static class StudentScoresAgent
{
    public const string Name = "StudentScoresAgent";

    public static AIAgent Create(ChatClient chatClient, StudentScoreTools tools) =>
        chatClient.AsAIAgent(
            name: Name,
            description: "Answers structured, numerical questions about student marks, averages, rankings and thresholds using deterministic tools.",
            instructions: """
                You are the Student Scores specialist for a school performance system.
                You answer questions about exact marks, averages, rankings and thresholds.

                Rules:
                - Always call the appropriate registered tool to get marks, averages, rankings or thresholds.
                - Never calculate, estimate, or invent a mark, average or ranking yourself - the tools are the
                  only source of truth.
                - If a tool result has Found = false, tell the user plainly that the student or subject was not
                  recognized, and list the known students/subjects from the tool result. Do not guess.
                - Keep answers concise and grounded strictly in the tool output.
                """,
            tools:
            [
                AIFunctionFactory.Create(tools.GetStudentScores),
                AIFunctionFactory.Create(tools.GetStudentAverage),
                AIFunctionFactory.Create(tools.GetHighestScorerBySubject),
                AIFunctionFactory.Create(tools.GetOverallTopStudent),
                AIFunctionFactory.Create(tools.GetStudentsAboveScore),
                AIFunctionFactory.Create(tools.GetClassAverageBySubject),
            ]);
}
