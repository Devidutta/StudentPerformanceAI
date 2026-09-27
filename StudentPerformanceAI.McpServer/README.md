# StudentPerformanceAI.McpServer

Exposes the capstone project's deterministic score tools and Chroma-backed semantic search as an
[MCP](https://modelcontextprotocol.io) server, over the **Streamable HTTP** transport, so any MCP
client (Claude Desktop, another agent, MCP Inspector) can call them directly - not just the
`StudentPerformanceAI` console app's own agents.

## Why a separate project

All real logic (Excel parsing, score calculations, Chroma search) stays in the sibling
`../StudentPerformanceAI.csproj` project, referenced here via `<ProjectReference>`. This project only
adds a thin `McpTools/` layer that wraps the existing `Tools.StudentScoreTools` /
`Tools.StudentKnowledgeTools` classes with `[McpServerToolType]` / `[McpServerTool]` metadata. The
original capstone project stays completely untouched - no new package references, no MCP awareness -
so it keeps satisfying the assessment's "no extra AI framework" constraint on its own.

## Tools exposed

| Tool | Wraps |
|---|---|
| `GetStudentScores`, `GetStudentAverage`, `GetHighestScorerBySubject`, `GetOverallTopStudent`, `GetStudentsAboveScore`, `GetClassAverageBySubject` | `StudentScoreTools` (deterministic C#, no LLM) |
| `SearchStudentObservations` | `StudentKnowledgeTools` (Chroma-backed semantic search) |

## Running

Requires the same prerequisites as the console app: a valid `OPENAI_API_KEY` and a running Chroma
server (see `../README.md` for how to start one). Both projects use the same `CHROMA_COLLECTION_NAME`
by default, so either process can index/query the same collection.

```bash
cd StudentPerformanceAI.McpServer
export OPENAI_API_KEY=sk-...
export CHROMA_BASE_URL=http://localhost:8000/api/v1/    # optional, this is the default
dotnet build
dotnet run
```

On success it prints:
```
MCP server listening at http://localhost:5259/mcp (Streamable HTTP). Point MCP Inspector at this URL.
```

Override the bind address with `MCP_SERVER_URL` (default `http://localhost:5259`).

## Testing with MCP Inspector

```bash
npx @modelcontextprotocol/inspector
```

In the Inspector UI: set transport to **Streamable HTTP**, URL to `http://localhost:5259/mcp`, connect,
then "List Tools" to see all 7, and try calling one, e.g.:
- `GetStudentAverage` with `studentName = "Mary"`
- `SearchStudentObservations` with `query = "Who needs help with numbers?"`

## From localhost to a real deployment

This is intentionally the same code you'd deploy remotely later (e.g. containerized on EKS behind an
ALB/Ingress): only the bind address, TLS termination, and adding an auth layer (MCP itself has no
built-in auth - `ModelContextProtocol.AspNetCore` supports plugging in ASP.NET Core authentication via
`AddMcp()` on an `AuthenticationBuilder`) would change; the tool registration and business logic stay
identical.

## Known limitations

- No authentication is configured; this is meant for local development against `localhost` only.
- Like the console app, it requires an externally-running Chroma server pinned to the legacy v1 API
  (see the main `README.md`).
