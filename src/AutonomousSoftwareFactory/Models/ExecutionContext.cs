namespace AutonomousSoftwareFactory.Models;

/// <summary>
/// Shared state passed through workflow steps.
/// Properties: inputs, shared_state, current_step.
/// </summary>
public class ExecutionContext
{
    public Dictionary<string, object> Inputs { get; set; } = new();
    public Dictionary<string, object> SharedState { get; set; } = new();
    public string? CurrentStep { get; set; }
}
