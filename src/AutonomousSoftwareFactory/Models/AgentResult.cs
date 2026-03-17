namespace AutonomousSoftwareFactory.Models;

public class AgentResult
{
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}
