using System.ComponentModel;
using ModelContextProtocol.Server;
using StudentPerformanceAI.Tools;

namespace StudentPerformanceAI.McpServer.McpTools;

/// <summary>
/// MCP-facing wrapper around the capstone project's <see cref="StudentKnowledgeTools"/>, which performs
/// Chroma-backed semantic search over teacher observations. All logic lives in the referenced
/// StudentPerformanceAI project; this class only adds MCP tool metadata.
/// </summary>
[McpServerToolType]
public sealed class StudentKnowledgeMcpTools(StudentKnowledgeTools tools)
{
    [McpServerTool, Description(
        "Searches teacher observation notes for evidence relevant to a meaning-based question about a " +
        "student's strengths, development areas, or recommended actions. Returns the most relevant matching " +
        "notes, or a clear 'not found' result if nothing relevant exists. Always call this before answering " +
        "any meaning-based question - never invent strengths, development areas or recommendations.")]
    public Task<string> SearchStudentObservations(
        [Description("The user's question or topic, e.g. 'Who needs help with numbers?'")] string query) =>
        tools.SearchStudentObservations(query);
}
