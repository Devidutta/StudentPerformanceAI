using System.ComponentModel;
using System.Text.Json;
using StudentPerformanceAI.Services;

namespace StudentPerformanceAI.Tools;

/// <summary>Semantic-search tool backing the Student Knowledge Agent. Grounds answers in retrieved note chunks only.</summary>
public sealed class StudentKnowledgeTools(SemanticSearchService search)
{
    [Description(
        "Searches teacher observation notes for evidence relevant to a meaning-based question about a " +
        "student's strengths, development areas, or recommended actions. Returns the most relevant matching " +
        "notes, or a clear 'not found' result if nothing relevant exists. Always call this before answering " +
        "any meaning-based question - never invent strengths, development areas or recommendations.")]
    public async Task<string> SearchStudentObservations(
        [Description("The user's question or topic, e.g. 'Who needs help with numbers?'")] string query)
    {
        var results = await search.SearchAsync(query);
        if (results.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                Found = false,
                Message = "No relevant teacher observations were found for this question."
            });
        }

        return JsonSerializer.Serialize(new
        {
            Found = true,
            Evidence = results.Select(r => new
            {
                r.StudentName,
                r.Section,
                r.Text,
                Relevance = Math.Round(r.Score, 3)
            })
        });
    }
}
