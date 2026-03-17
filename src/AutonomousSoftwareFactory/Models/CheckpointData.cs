namespace AutonomousSoftwareFactory.Models;

public class CheckpointData
{
    public string WorkflowName { get; set; } = string.Empty;
    public List<string> CompletedSteps { get; set; } = [];
    public string? LastCompletedStep { get; set; }
    public Dictionary<string, object> Inputs { get; set; } = new();
    public Dictionary<string, object> StateData { get; set; } = new();
    public DateTime Timestamp { get; set; }
}
