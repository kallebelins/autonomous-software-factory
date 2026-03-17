namespace AutonomousSoftwareFactory.Models;

public class ToolExecutionRequest
{
    public ToolDefinition Tool { get; set; } = new();
    public Dictionary<string, string> Inputs { get; set; } = new();
    public string WorkingDirectory { get; set; } = string.Empty;
}
