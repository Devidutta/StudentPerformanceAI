using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using StudentPerformanceAI.Agents;
using StudentPerformanceAI.Models;
using StudentPerformanceAI.Services;
using StudentPerformanceAI.Tools;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

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
        Project = OptionalValue("LANGSMITH_PROJECT", "LangSmith:Project", "student-performance-ai")!,
        BaseUrl = OptionalValue("LANGSMITH_BASE_URL", "LangSmith:BaseUrl", "https://api.smith.langchain.com")!
    };

    var dataPaths = new DataPathOptions
    {
        ScoresWorkbook = Path.Combine(AppContext.BaseDirectory, OptionalValue("SCORES_WORKBOOK_PATH", "DataPaths:ScoresWorkbook", "Data/StudentScores.xlsx")!),
        PerformanceNotes = Path.Combine(AppContext.BaseDirectory, OptionalValue("PERFORMANCE_NOTES_PATH", "DataPaths:PerformanceNotes", "Data/StudentPerformanceNotes.txt")!),
        EmbeddingCache = Path.Combine(AppContext.BaseDirectory, OptionalValue("EMBEDDING_CACHE_PATH", "DataPaths:EmbeddingCache", "Data/embedding-cache.json")!)
    };

    using var tracing = new TracingService(langSmithOptions);
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

    Console.WriteLine("Indexing teacher observations for semantic search...");
    var embeddingCache = new EmbeddingCacheService(dataPaths.EmbeddingCache);
    var semanticSearch = new SemanticSearchService(embeddingClient, embeddingCache, tracing);
    await semanticSearch.InitializeAsync(dataPaths.PerformanceNotes);

    var scoreTools = new StudentScoreTools(dataStore);
    var knowledgeTools = new StudentKnowledgeTools(semanticSearch);

    var scoresAgent = StudentScoresAgent.Create(chatClient, scoreTools);
    var knowledgeAgent = StudentKnowledgeAgent.Create(chatClient, knowledgeTools);
    var coordinator = CoordinatorAgent.Create(chatClient, scoresAgent, knowledgeAgent);

    PrintWelcome(dataStore);
    await RunReplAsync(coordinator, tracing);
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
catch (Exception ex)
{
    Console.Error.WriteLine($"An unexpected error occurred: {ex.Message}");
    Environment.ExitCode = 1;
}

static void PrintWelcome(StudentDataStore dataStore)
{
    Console.WriteLine();
    Console.WriteLine("Student Performance AI System");
    Console.WriteLine("Ask a question about student scores or teacher observations.");
    Console.WriteLine("Type 'help' for examples, or 'exit' to quit.");
    Console.WriteLine();
}

static void PrintHelp()
{
    Console.WriteLine();
    Console.WriteLine("Example questions:");
    Console.WriteLine("  Show Mary's subject-wise scores.");
    Console.WriteLine("  What is Mary's average score?");
    Console.WriteLine("  Who scored highest in Mathematics?");
    Console.WriteLine("  Who is the overall top-performing student?");
    Console.WriteLine("  Who needs help with numbers?");
    Console.WriteLine("  What are John's strengths?");
    Console.WriteLine("  How is Mary performing overall, and what should she improve?");
    Console.WriteLine("Commands: 'help', 'exit' / 'quit'.");
    Console.WriteLine();
}

static async Task RunReplAsync(AIAgent coordinator, TracingService tracing)
{
    while (true)
    {
        Console.Write("> ");
        var input = Console.ReadLine();
        if (input is null)
        {
            break;
        }

        var trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            continue;
        }

        if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        if (trimmed.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            continue;
        }

        await using var span = tracing.StartSpan("coordinator_ask", "chain", new { question = trimmed });
        try
        {
            var response = await coordinator.RunAsync(trimmed);
            Console.WriteLine(response.Text);
            await span.EndAsync(new { answer = response.Text });
        }
        catch (ClientResultException ex) when (ex.Status is 401 or 403)
        {
            Console.WriteLine("Sorry, the AI service rejected the request due to an authentication problem. Please check the configured API key.");
            await span.EndAsync(error: "authentication error");
        }
        catch (ClientResultException ex)
        {
            Console.WriteLine($"Sorry, the AI service is temporarily unavailable (HTTP {ex.Status}). Please try again.");
            await span.EndAsync(error: $"http {ex.Status}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Sorry, something went wrong while answering that question. Please try again.");
            await span.EndAsync(error: ex.Message);
        }

        Console.WriteLine();
    }
}
