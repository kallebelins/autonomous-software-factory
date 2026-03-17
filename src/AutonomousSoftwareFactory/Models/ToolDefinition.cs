namespace AutonomousSoftwareFactory.Models;

/// <summary>
/// Root document for deserializing tools.yaml.
/// </summary>
public class ToolsDocument
{
    public string Version { get; set; } = string.Empty;
    public List<ToolDefinition> Tools { get; set; } = new();
}

/// <summary>
/// Represents a tool definition from tools.yaml.
/// Properties: name, category, description, execution_type, input, output, command, api, constraints.
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExecutionType { get; set; } = string.Empty;
    public Dictionary<string, string> Input { get; set; } = new();
    public Dictionary<string, string> Output { get; set; } = new();
    public string? Command { get; set; }
    public ApiDefinition? Api { get; set; }
    public List<string>? Constraints { get; set; }
}

/// <summary>
/// Represents the API configuration for tools with execution_type "api".
/// </summary>
public class ApiDefinition
{
    public string Provider { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public Dictionary<string, string> Body { get; set; } = new();
}
