# Student Performance AI System

ASCEND Week 11 capstone: a .NET 10 console app that answers both exact score questions and
meaning-based questions about students, using the Microsoft Agent Framework, OpenAI, and a local
semantic search index over teacher observations. No Semantic Kernel, no database.

## Architecture

```
User question
  -> CoordinatorAgent (OpenAI + Microsoft Agent Framework)
       -> StudentScoresAgent  --calls-->  deterministic C# tools (Tools/StudentScoreTools.cs)
                                            over data loaded from Data/StudentScores.xlsx
       -> StudentKnowledgeAgent --calls--> SearchStudentObservations tool (Tools/StudentKnowledgeTools.cs)
                                            over an in-memory embedding index built from
                                            Data/StudentPerformanceNotes.txt
  -> grounded, combined natural-language response
```

- **CoordinatorAgent** (`Agents/CoordinatorAgent.cs`) decides whether a question is structured,
  meaning-based, combined, or unsupported, and calls the two specialist agents as tools
  (`AIAgent.AsAIFunction`) - this keeps the three responsibilities in separate agent objects while
  staying entirely within the Microsoft Agent Framework (no Semantic Kernel).
- **StudentScoresAgent** (`Agents/StudentScoresAgent.cs`) only ever calls the six deterministic C#
  score tools in `Tools/StudentScoreTools.cs`. It never calculates a mark itself.
- **StudentKnowledgeAgent** (`Agents/StudentKnowledgeAgent.cs`) only ever calls
  `SearchStudentObservations` (`Tools/StudentKnowledgeTools.cs`), which performs the semantic search
  and returns grounded evidence chunks.

### Where deterministic logic / retrieval / the LLM are used

| Concern | Implementation |
|---|---|
| Reading marks | `Services/ExcelStudentService.cs` reads `StudentScores.xlsx` with ClosedXML; subject columns are discovered from the header row at runtime - nothing is hard-coded. |
| Calculations (average, ranking, thresholds) | Plain C# in `Tools/StudentScoreTools.cs`. The LLM only chooses which tool to call and phrases the final sentence; it never computes a number. |
| Teacher-observation retrieval | `Services/SemanticSearchService.cs` chunks `StudentPerformanceNotes.txt`, embeds each chunk with the OpenAI embeddings API, and ranks matches by cosine similarity. No external search service or database. |
| Natural-language understanding & phrasing | OpenAI chat model, via `Microsoft.Agents.AI.OpenAI`, for all three agents. |
| Routing / orchestration | Microsoft Agent Framework (`ChatClient.AsAIAgent`, `AIAgent.AsAIFunction`). |
| Tracing | `Services/TracingService.cs` posts spans directly to the LangSmith REST API (see below). |

### Semantic search: local, not an external service

The original assessment brief assumes a trainer-hosted semantic-search API
(`SEMANTIC_SEARCH_BASE_URL` etc.). Since this was built independently, semantic search is instead
implemented **locally, in-process**:
1. `StudentPerformanceNotes.txt` is split into per-student, per-section chunks (Strengths /
   Development Areas / Recommended Actions) - this parsing is pure string/regex logic, not an LLM call.
2. Each chunk is embedded once via OpenAI's embeddings API and cached to `Data/embedding-cache.json`
   (gitignored) so restarts don't re-embed unchanged content.
3. A query is embedded and matched against the cache via cosine similarity; results below a relevance
   threshold are treated as "not found" so the agent can say so honestly.

This avoids introducing a database or an extra hosted service, per the assessment's constraints.

### Tracing with LangSmith

There is no official LangSmith .NET SDK, so `Services/TracingService.cs` is a small `HttpClient`
wrapper that POSTs/PATCHes LangSmith's `runs` REST endpoints directly, recording:
- one top-level run per user question (`coordinator_ask`)
- one run per embedding call made while indexing/searching notes (`embed_chunk`)

Tracing is entirely optional: if `LANGSMITH_API_KEY` is not set, the tracer no-ops and the app
behaves identically. Tracing calls never log the API keys themselves, and failures in the tracing
HTTP calls are swallowed so they can never break or slow down the console app.

## Project structure

```
StudentPerformanceAI/
├── Agents/            CoordinatorAgent, StudentScoresAgent, StudentKnowledgeAgent
├── Data/               StudentScores.xlsx, StudentPerformanceNotes.txt
├── Models/             Student, SemanticSearchResult, OpenAIOptions, LangSmithOptions, DataPathOptions
├── Services/           ExcelStudentService, StudentDataStore, SemanticSearchService,
│                       EmbeddingCacheService, TracingService
├── Tools/              StudentScoreTools (6 deterministic tools), StudentKnowledgeTools (search tool)
├── Program.cs
├── appsettings.json    non-secret defaults only (model names, paths) - real secrets come from env vars
├── StudentPerformanceAI.csproj
└── .gitignore
```

## Configuration

All secrets are supplied via environment variables (or an untracked `appsettings.Local.json` for
local development only - it is gitignored and must never be committed).

| Variable | Required | Purpose |
|---|---|---|
| `OPENAI_API_KEY` | yes | OpenAI API key |
| `OPENAI_BASE_URL` | no | Override the OpenAI endpoint (e.g. an Azure OpenAI-compatible proxy) |
| `OPENAI_MODEL` | no (default `gpt-4o-mini`) | Chat model used by all three agents |
| `OPENAI_EMBEDDING_MODEL` | no (default `text-embedding-3-small`) | Embedding model for semantic search |
| `LANGSMITH_API_KEY` | no | Enables LangSmith tracing when set |
| `LANGSMITH_PROJECT` | no (default `student-performance-ai`) | LangSmith project name |
| `LANGSMITH_BASE_URL` | no (default `https://api.smith.langchain.com`) | LangSmith API base URL |

## Running

```bash
cd StudentPerformanceAI
export OPENAI_API_KEY=sk-...           # PowerShell: $env:OPENAI_API_KEY = "sk-..."
export LANGSMITH_API_KEY=lsv2_...      # optional, enables tracing
dotnet build
dotnet run
```

Commands inside the app: `help` (show example questions), `exit` / `quit`.

## Test evidence

Fill this section in with real transcripts after running the app with a valid `OPENAI_API_KEY` (see
doc §13). Suggested set, matching the assessment's minimums:

**Structured (3)**
- `Show Mary's subject-wise scores.`
- `What is Mary's average score?`
- `Who scored highest in Mathematics?`

**Meaning-based (3)**
- `Who needs help with numbers?`
- `Who demonstrates strong logical thinking?`
- `Who understands practical experiments?`

**Combined (2)**
- `How is Mary performing overall, and what should she improve?`
- `Show John's marks and explain his main development need.`

**Error/boundary (2)**
- `Show Robert's scores.`
- `What is the weather today?`

**No-hard-coding proof**
1. Edit one mark in `Data/StudentScores.xlsx`.
2. Restart the app (`dotnet run`).
3. Re-ask the related question (e.g. `What is Mary's average score?`).
4. Confirm the answer reflects the updated value.

**Same intent, two phrasings**
- `What is Mary's average?`
- `How is Mary performing overall based on her marks?`

## Known limitations & future improvements

- Semantic search is in-memory and rebuilt from a flat text file rather than a managed knowledge
  base; it scales to a small class roster but not to a large corpus of notes.
- The embedding cache is a local JSON file, not shared across machines/deployments.
- LangSmith tracing is a hand-rolled REST client rather than an official SDK; it captures the
  coordinator call and embedding calls but not every intermediate model turn.
- Future improvement: swap the local embedding index for a real vector store, and add streaming
  responses for a more responsive console experience.
