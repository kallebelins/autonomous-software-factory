namespace AutonomousSoftwareFactory.Models;

public class AgentExecutionRequest
{
    public AgentDefinition Agent { get; set; } = new();
    public Dictionary<string, object> Inputs { get; set; } = new();
    public List<SkillDefinition> Skills { get; set; } = new();
    public List<ToolDefinition> Tools { get; set; } = new();
    public string Prompt { get; set; } = string.Empty;
    public ExecutionContext Context { get; set; } = new();
}
