namespace AutonomousSoftwareFactory.Logging;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public partial class FileRunLogger : IRunLogger, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public string FilePath { get; }

    public FileRunLogger(string logDirectory, string workflowName)
    {
        Directory.CreateDirectory(logDirectory);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var safeName = SafeFileName().Replace(workflowName, "_");
        FilePath = Path.Combine(logDirectory, $"{timestamp}-{safeName}.log");

        var stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    public void LogWorkflowStart(string workflowName, int stepCount)
    {
        Write("WORKFLOW_START", new Dictionary<string, object>
        {
            ["workflow"] = workflowName,
            ["steps"] = stepCount
        });
    }

    public void LogWorkflowEnd(string workflowName, string status, TimeSpan duration)
    {
        Write("WORKFLOW_END", new Dictionary<string, object>
        {
            ["workflow"] = workflowName,
            ["status"] = status,
            ["duration_ms"] = Math.Round(duration.TotalMilliseconds, 2)
        });
    }

    public void LogStepStart(string stepId, string stepName, string type, int attempt, int maxAttempts)
    {
        Write("STEP_START", new Dictionary<string, object>
        {
            ["step_id"] = stepId,
            ["step_name"] = stepName,
            ["type"] = type,
            ["attempt"] = attempt,
            ["max_attempts"] = maxAttempts
        });
    }

    public void LogStepEnd(string stepId, string status, TimeSpan duration, List<string>? errors = null)
    {
        var data = new Dictionary<string, object>
        {
            ["step_id"] = stepId,
            ["status"] = status,
            ["duration_ms"] = Math.Round(duration.TotalMilliseconds, 2)
        };

        if (errors is { Count: > 0 })
            data["errors"] = errors;

        Write("STEP_END", data);
    }

    public void LogLlmCall(string model, string promptSummary, string responseSummary, int tokens, double durationMs)
    {
        Write("LLM_CALL", new Dictionary<string, object>
        {
            ["model"] = model,
            ["prompt"] = promptSummary,
            ["response"] = responseSummary,
            ["tokens"] = tokens,
            ["duration_ms"] = Math.Round(durationMs, 2)
        });
    }

    public void LogToolExecution(string toolName, string executionType, string? command, bool success, string? output, List<string>? errors = null)
    {
        var data = new Dictionary<string, object>
        {
            ["tool"] = toolName,
            ["execution_type"] = executionType,
            ["success"] = success
        };

        if (command is not null)
            data["command"] = command;

        if (output is not null)
        {
            var summary = output.Length > 500 ? output[..500] + "..." : output;
            data["output"] = summary;
        }

        if (errors is { Count: > 0 })
            data["errors"] = errors;

        Write("TOOL_EXEC", data);
    }

    private void Write(string category, Dictionary<string, object> data)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var line = $"[{timestamp}] [{category}] {json}";

        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        _writer.Dispose();
        GC.SuppressFinalize(this);
    }

    [GeneratedRegex(@"[^\w\-.]")]
    private static partial Regex SafeFileName();
}
