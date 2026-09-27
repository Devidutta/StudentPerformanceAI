using System.Net.Http.Json;
using System.Text.Json;
using StudentPerformanceAI.Models;

namespace StudentPerformanceAI.Services;

/// <summary>
/// Lightweight tracer that posts run/span events to LangSmith's REST API (https://api.smith.langchain.com).
/// There is no official LangSmith .NET SDK, so this talks to the "runs" endpoints directly.
/// Tracing is best-effort: if LANGSMITH_API_KEY is not configured, or any HTTP call fails, spans
/// silently no-op so the console app never fails or slows down because of tracing.
/// </summary>
public sealed class TracingService : IDisposable
{
    private readonly HttpClient _http;
    private readonly LangSmithOptions _options;

    public TracingService(LangSmithOptions options)
    {
        _options = options;
        _http = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
        if (options.IsEnabled)
        {
            _http.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
        }
    }

    /// <summary>Starts a traced span. Dispose (or call EndAsync) to close it.</summary>
    public TraceSpan StartSpan(string name, string runType, object? inputs = null, string? parentRunId = null) =>
        new(this, name, runType, inputs, parentRunId);

    internal async Task PostRunStartAsync(TraceSpan span)
    {
        if (!_options.IsEnabled)
        {
            return;
        }

        try
        {
            await _http.PostAsJsonAsync("api/v1/runs", new
            {
                id = span.RunId,
                trace_id = span.TraceId,
                parent_run_id = span.ParentRunId,
                name = span.Name,
                run_type = span.RunType,
                project_name = _options.Project,
                inputs = span.Inputs ?? new { },
                start_time = span.StartedAt.ToString("o")
            });
        }
        catch
        {
            // Tracing must never break the application.
        }
    }

    internal async Task PostRunEndAsync(TraceSpan span, object? outputs, string? error)
    {
        if (!_options.IsEnabled)
        {
            return;
        }

        try
        {
            await _http.PatchAsync($"api/v1/runs/{span.RunId}", JsonContent.Create(new
            {
                outputs = outputs ?? new { },
                error,
                end_time = DateTimeOffset.UtcNow.ToString("o")
            }));
        }
        catch
        {
            // Tracing must never break the application.
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Represents one traced unit of work (agent call, tool call, LLM/embedding call).</summary>
public sealed class TraceSpan : IAsyncDisposable
{
    private readonly TracingService _tracer;
    private bool _ended;

    public string RunId { get; } = Guid.NewGuid().ToString();
    public string TraceId { get; }
    public string? ParentRunId { get; }
    public string Name { get; }
    public string RunType { get; }
    public object? Inputs { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    internal TraceSpan(TracingService tracer, string name, string runType, object? inputs, string? parentRunId)
    {
        _tracer = tracer;
        Name = name;
        RunType = runType;
        Inputs = inputs;
        ParentRunId = parentRunId;
        TraceId = parentRunId ?? RunId;
        _ = _tracer.PostRunStartAsync(this);
    }

    public async Task EndAsync(object? outputs = null, string? error = null)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        await _tracer.PostRunEndAsync(this, outputs, error);
    }

    public async ValueTask DisposeAsync() => await EndAsync();
}
