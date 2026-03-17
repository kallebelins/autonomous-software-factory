namespace AutonomousSoftwareFactory.Logging;

public interface IRunLogger
{
    void LogWorkflowStart(string workflowName, int stepCount);
    void LogWorkflowEnd(string workflowName, string status, TimeSpan duration);
    void LogStepStart(string stepId, string stepName, string type, int attempt, int maxAttempts);
    void LogStepEnd(string stepId, string status, TimeSpan duration, List<string>? errors = null);
    void LogLlmCall(string model, string promptSummary, string responseSummary, int tokens, double durationMs);
    void LogToolExecution(string toolName, string executionType, string? command, bool success, string? output, List<string>? errors = null);
}
