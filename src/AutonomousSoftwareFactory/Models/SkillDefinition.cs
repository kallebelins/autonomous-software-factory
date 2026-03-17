namespace AutonomousSoftwareFactory.Models;

/// <summary>
/// Root document for deserializing skills_registry.yaml.
/// </summary>
public class SkillsDocument
{
    public string Version { get; set; } = string.Empty;
    public List<SkillDefinition> Skills { get; set; } = new();
}

/// <summary>
/// Represents a skill definition from skills_registry.yaml.
/// Properties: name, type (cognitive|operational), description, instructions, expected_input, expected_output, constraints, tools.
/// </summary>
public class SkillDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public List<string> ExpectedInput { get; set; } = new();
    public List<string> ExpectedOutput { get; set; } = new();
    public List<string> Constraints { get; set; } = new();
    public List<string>? Tools { get; set; }
}
