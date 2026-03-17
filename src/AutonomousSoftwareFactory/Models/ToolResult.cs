namespace AutonomousSoftwareFactory.Models;

public class ToolResult
{
    public bool Success { get; set; }
    public Dictionary<string, object> Output { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
