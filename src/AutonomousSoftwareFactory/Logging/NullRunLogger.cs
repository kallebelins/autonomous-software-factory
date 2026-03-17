namespace AutonomousSoftwareFactory.Logging;

public class NullRunLogger : IRunLogger
{
    public static NullRunLogger Instance { get; } = new();

    public void LogWorkflowStart(string workflowName, int stepCount) { }
    public void LogWorkflowEnd(string workflowName, string status, TimeSpan duration) { }
    public void LogStepStart(string stepId, string stepName, string type, int attempt, int maxAttempts) { }
    public void LogStepEnd(string stepId, string status, TimeSpan duration, List<string>? errors = null) { }
    public void LogLlmCall(string model, string promptSummary, string responseSummary, int tokens, double durationMs) { }
    public void LogToolExecution(string toolName, string executionType, string? command, bool success, string? output, List<string>? errors = null) { }
}
