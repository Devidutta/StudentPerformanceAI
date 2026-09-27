using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using StudentPerformanceAI.Tools;

namespace StudentPerformanceAI.Agents;

/// <summary>Specialist agent for meaning-based questions about teacher observations. Grounded in retrieved evidence only.</summary>
public static class StudentKnowledgeAgent
{
    public const string Name = "StudentKnowledgeAgent";

    public static AIAgent Create(ChatClient chatClient, StudentKnowledgeTools tools) =>
        chatClient.AsAIAgent(
            name: Name,
            description: "Answers meaning-based questions about student strengths, development areas and recommended actions, grounded in retrieved teacher observations.",
            instructions: """
                You are the Student Knowledge specialist for a school performance system. You answer
                meaning-based questions about student strengths, development areas and recommended actions,
                using teacher observation notes.

                Rules:
                - Always call SearchStudentObservations to retrieve evidence before answering.
                - Base your answer only on the retrieved evidence text - never invent a strength, development
                  area, recommendation, or student that isn't in the evidence.
                - If the tool result has Found = false, or the evidence doesn't actually answer the question,
                  say plainly that no relevant observation was found. Do not guess.
                - If the question is genuinely ambiguous (e.g. it could refer to more than one student or
                  concept), ask a brief clarifying question instead of guessing.
                - Keep answers concise.
                """,
            tools: [AIFunctionFactory.Create(tools.SearchStudentObservations)]);
}
