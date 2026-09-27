using System.ClientModel;
using ChromaDB.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using StudentPerformanceAI.McpServer.McpTools;
using StudentPerformanceAI.Models;
using StudentPerformanceAI.Services;
using StudentPerformanceAI.Tools;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;

string RequireValue(string envVarName, string configPath, string friendlyName)
{
    var value = configuration[envVarName];
    if (string.IsNullOrWhiteSpace(value))
    {
        value = configuration[configPath];
    }

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Missing required configuration for {friendlyName}. Set the '{envVarName}' environment " +
            $"variable (or '{configPath}' in appsettings.Local.json).");
    }

    return value;
}

string? OptionalValue(string envVarName, string configPath, string? defaultValue = null) =>
    configuration[envVarName] ?? configuration[configPath] ?? defaultValue;

try
{
    var openAiOptions = new OpenAIOptions
    {
        ApiKey = RequireValue("OPENAI_API_KEY", "OpenAI:ApiKey", "the OpenAI API key"),
        BaseUrl = OptionalValue("OPENAI_BASE_URL", "OpenAI:BaseUrl"),
        Model = OptionalValue("OPENAI_MODEL", "OpenAI:Model", "gpt-4o-mini")!,
        EmbeddingModel = OptionalValue("OPENAI_EMBEDDING_MODEL", "OpenAI:EmbeddingModel", "text-embedding-3-small")!
    };

    var langSmithOptions = new LangSmithOptions
    {
        ApiKey = OptionalValue("LANGSMITH_API_KEY", "LangSmith:ApiKey"),
        Project = OptionalValue("LANGSMITH_PROJECT", "LangSmith:Project", "student-performance-ai-mcp")!,
        BaseUrl = OptionalValue("LANGSMITH_BASE_URL", "LangSmith:BaseUrl", "https://api.smith.langchain.com")!
    };

    var dataPaths = new DataPathOptions
    {
        ScoresWorkbook = Path.Combine(AppContext.BaseDirectory, OptionalValue("SCORES_WORKBOOK_PATH", "DataPaths:ScoresWorkbook", "Data/StudentScores.xlsx")!),
        PerformanceNotes = Path.Combine(AppContext.BaseDirectory, OptionalValue("PERFORMANCE_NOTES_PATH", "DataPaths:PerformanceNotes", "Data/StudentPerformanceNotes.txt")!)
    };

    var chromaOptions = new ChromaOptions
    {
        BaseUrl = OptionalValue("CHROMA_BASE_URL", "Chroma:BaseUrl", "http://localhost:8000/api/v1/")!,
        CollectionName = OptionalValue("CHROMA_COLLECTION_NAME", "Chroma:CollectionName", "student-performance-notes")!,
        AuthToken = OptionalValue("CHROMA_AUTH_TOKEN", "Chroma:AuthToken"),
        Tenant = OptionalValue("CHROMA_TENANT", "Chroma:Tenant"),
        Database = OptionalValue("CHROMA_DATABASE", "Chroma:Database")
    };

    var mcpUrl = OptionalValue("MCP_SERVER_URL", "Mcp:Url", "http://localhost:5259")!;
    builder.WebHost.UseUrls(mcpUrl);

    var tracing = new TracingService(langSmithOptions);
    Console.WriteLine(langSmithOptions.IsEnabled
        ? $"LangSmith tracing enabled (project '{langSmithOptions.Project}')."
        : "LangSmith tracing disabled (LANGSMITH_API_KEY not set).");

    var openAiClientOptions = new OpenAIClientOptions();
    if (!string.IsNullOrWhiteSpace(openAiOptions.BaseUrl))
    {
        openAiClientOptions.Endpoint = new Uri(openAiOptions.BaseUrl);
    }

    var openAiClient = new OpenAIClient(new ApiKeyCredential(openAiOptions.ApiKey), openAiClientOptions);
    var chatClient = openAiClient.GetChatClient(openAiOptions.Model);
    var embeddingClient = openAiClient.GetEmbeddingClient(openAiOptions.EmbeddingModel);

    Console.WriteLine("Loading student scores from workbook...");
    var dataStore = new StudentDataStore(dataPaths.ScoresWorkbook);
    Console.WriteLine($"Loaded {dataStore.Students.Count} students across subjects: {string.Join(", ", dataStore.Subjects)}.");

    Console.WriteLine($"Indexing teacher observations into Chroma collection '{chromaOptions.CollectionName}' at {chromaOptions.BaseUrl}...");
    var semanticSearch = new SemanticSearchService(embeddingClient, chromaOptions, tracing);
    await semanticSearch.InitializeAsync(dataPaths.PerformanceNotes);

    var scoreMcpTools = new StudentScoreMcpTools(new StudentScoreTools(dataStore));
    var knowledgeMcpTools = new StudentKnowledgeMcpTools(new StudentKnowledgeTools(semanticSearch));

    builder.Services.AddSingleton(scoreMcpTools);
    builder.Services.AddSingleton(knowledgeMcpTools);

    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "student-performance-ai", Version = "1.0.0" };
        })
        .WithHttpTransport()
        .WithTools<StudentScoreMcpTools>()
        .WithTools<StudentKnowledgeMcpTools>();

    var app = builder.Build();
    app.MapMcp("/mcp");

    Console.WriteLine($"MCP server listening at {mcpUrl}/mcp (Streamable HTTP). Point MCP Inspector at this URL.");
    await app.RunAsync();
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine($"Data file error: {ex.Message}");
    Environment.ExitCode = 1;
}
catch (InvalidDataException ex)
{
    Console.Error.WriteLine($"Data validation error: {ex.Message}");
    Environment.ExitCode = 1;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Configuration error: {ex.Message}");
    Environment.ExitCode = 1;
}
catch (ClientResultException ex) when (ex.Status is 401 or 403)
{
    Console.Error.WriteLine("Authentication error: the configured OpenAI API key was rejected. Check OPENAI_API_KEY.");
    Environment.ExitCode = 1;
}
catch (ClientResultException ex)
{
    Console.Error.WriteLine($"The AI service returned an error (HTTP {ex.Status}). Please try again shortly.");
    Environment.ExitCode = 1;
}
catch (Exception ex) when (ex is ChromaException or HttpRequestException)
{
    Console.Error.WriteLine(
        "Vector database error: could not reach the Chroma server. Check CHROMA_BASE_URL and that the " +
        $"server is running. ({ex.Message})");
    Environment.ExitCode = 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"An unexpected error occurred: {ex.Message}");
    Environment.ExitCode = 1;
}
