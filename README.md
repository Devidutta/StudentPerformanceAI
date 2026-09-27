# Student Performance AI System

ASCEND Week 11 capstone: a .NET 10 console app that answers both exact score questions and
meaning-based questions about students, using the Microsoft Agent Framework, OpenAI, and a Chroma
vector database for semantic search over teacher observations. No Semantic Kernel.

## Architecture

```
User question
  -> CoordinatorAgent (OpenAI + Microsoft Agent Framework)
       -> StudentScoresAgent  --calls-->  deterministic C# tools (Tools/StudentScoreTools.cs)
                                            over data loaded from Data/StudentScores.xlsx
       -> StudentKnowledgeAgent --calls--> SearchStudentObservations tool (Tools/StudentKnowledgeTools.cs)
                                            over a Chroma collection built from
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
| Teacher-observation retrieval | `Services/SemanticSearchService.cs` chunks `StudentPerformanceNotes.txt`, embeds each chunk with the OpenAI embeddings API, and stores/searches them in a Chroma vector database collection via `Services/ChromaApiClient.cs`. |
| Natural-language understanding & phrasing | OpenAI chat model, via `Microsoft.Agents.AI.OpenAI`, for all three agents. |
| Routing / orchestration | Microsoft Agent Framework (`ChatClient.AsAIAgent`, `AIAgent.AsAIFunction`). |
| Tracing | `Services/TracingService.cs` posts spans directly to the LangSmith REST API (see below). |

### Vector database (Chroma)

The original assessment brief assumes a trainer-hosted semantic-search API
(`SEMANTIC_SEARCH_BASE_URL` etc.). This build instead uses a real vector database - **Chroma** - as
its knowledge-base store:
1. `StudentPerformanceNotes.txt` is split into per-student, per-section chunks (Strengths /
   Development Areas / Recommended Actions) - this parsing is pure string/regex logic, not an LLM call.
2. Each chunk gets a stable id (`SHA256("{StudentName}|{Section}")`) and is upserted into a single
   Chroma collection (`CHROMA_COLLECTION_NAME`, default `student-performance-notes`), created with
   `hnsw:space = cosine` so nearest-neighbor distance maps directly to cosine similarity.
3. On every startup, `SemanticSearchService.InitializeAsync` fetches the existing documents for those
   ids and only calls the OpenAI embeddings API (and re-upserts) for chunks whose text actually
   changed - unchanged notes cost zero embedding calls on restart, and there is no separate local
   cache file to manage.
4. A query is embedded once and matched via `ChromaCollectionClient.Query(...)`; results below a
   relevance threshold are treated as "not found" so the agent can say so honestly.

There is no official .NET SDK for Chroma, and the only community package (`ChromaDB.Client`) only
speaks Chroma's legacy v1 HTTP API - which **Chroma Cloud does not serve at all** (confirmed live:
`GET /api/v1/heartbeat` on Chroma Cloud returns `410 {"message":"The v1 API is deprecated. Please use
/v2 apis"}`). So `Services/ChromaApiClient.cs` instead talks to Chroma's **v2 REST API** directly over
a plain `HttpClient`, using the exact request/response shapes confirmed against Chroma Cloud's live
`openapi.json`. This works against both **Chroma Cloud** and a modern self-hosted server.

Tenant/database resolution: if `CHROMA_TENANT`/`CHROMA_DATABASE` are both set, they're used as-is.
Otherwise, the client calls `GET /api/v2/auth/identity` (using `CHROMA_AUTH_TOKEN`) to auto-resolve the
tenant and database a Chroma Cloud API key belongs to; if that call fails (e.g. a self-hosted server
with no auth configured, which may not implement that route), it falls back to Chroma's conventional
`default_tenant` / `default_database`.

#### Option A: Chroma Cloud

```bash
export CHROMA_BASE_URL=https://api.trychroma.com
export CHROMA_AUTH_TOKEN=ck-...                            # your Chroma Cloud API key
export CHROMA_COLLECTION_NAME=student-performance-notes    # optional, this is the default
```

#### Option B: self-hosted Chroma

```bash
pip install chromadb
chroma run --path ./chroma-data --port 8000
```

```bash
export CHROMA_BASE_URL=http://localhost:8000   # this is also the default if unset
```

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
├── Models/             Student, SemanticSearchResult, OpenAIOptions, LangSmithOptions, ChromaOptions,
│                       DataPathOptions
├── Services/           ExcelStudentService, StudentDataStore, SemanticSearchService, ChromaApiClient,
│                       TracingService
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
| `CHROMA_BASE_URL` | no (default `http://localhost:8000`) | Chroma server base URL (Chroma Cloud: `https://api.trychroma.com`) |
| `CHROMA_COLLECTION_NAME` | no (default `student-performance-notes`) | Chroma collection holding note chunks |
| `CHROMA_AUTH_TOKEN` | no (required for Chroma Cloud) | `x-chroma-token` value |
| `CHROMA_TENANT` / `CHROMA_DATABASE` | no | Skip auto-resolution and use these tenant/database names directly |

## Running

Either Chroma Cloud or a self-hosted server must be reachable first - see the Chroma options above.

```bash
cd StudentPerformanceAI
export OPENAI_API_KEY=sk-...           # PowerShell: $env:OPENAI_API_KEY = "sk-..."
export LANGSMITH_API_KEY=lsv2_...      # optional, enables tracing
export CHROMA_BASE_URL=https://api.trychroma.com   # or omit to use the localhost default
export CHROMA_AUTH_TOKEN=ck-...                    # required for Chroma Cloud
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

- Requires a reachable Chroma instance (Cloud or self-hosted); the app does not embed or manage the
  database process itself.
- `ChromaApiClient` is a small hand-rolled v2 REST client (there's no official .NET SDK for Chroma),
  covering only the operations this app needs (get-or-create collection, get, upsert, query) - not a
  general-purpose Chroma client.
- The knowledge base is still rebuilt from a single flat text file (`StudentPerformanceNotes.txt`);
  Chroma solves the *retrieval* scaling problem, but there's no ingestion pipeline yet for adding
  notes from other sources.
- LangSmith tracing is a hand-rolled REST client rather than an official SDK; it captures the
  coordinator call and embedding calls but not every intermediate model turn.
- Future improvements: add an ingestion pipeline for new/updated notes, and add streaming responses
  for a more responsive console experience.
