namespace AutonomousSoftwareFactory.Models;

/// <summary>
/// Final result of a workflow execution.
/// Properties: status, outputs, errors, duration.
/// </summary>
public class ExecutionResult
{
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object> Outputs { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
}
