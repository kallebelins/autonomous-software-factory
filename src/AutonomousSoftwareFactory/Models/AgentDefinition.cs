namespace AutonomousSoftwareFactory.Models;

/// <summary>
/// Root document for deserializing agents.yaml.
/// </summary>
public class AgentsDocument
{
    public string Version { get; set; } = string.Empty;
    public List<AgentDefinition> Agents { get; set; } = new();
}

/// <summary>
/// Represents an agent definition from agents.yaml.
/// Properties: name, description, status, responsibilities, input, output, skills, tools, prompt.
/// </summary>
public class AgentDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Note { get; set; }
    public List<string> Responsibilities { get; set; } = new();
    public List<string> Input { get; set; } = new();
    public List<string> Output { get; set; } = new();
    public List<string> Skills { get; set; } = new();
    public List<string> Tools { get; set; } = new();
    public string Prompt { get; set; } = string.Empty;
}
