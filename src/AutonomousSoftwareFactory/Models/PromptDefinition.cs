namespace AutonomousSoftwareFactory.Models;

using YamlDotNet.Serialization;

/// <summary>
/// Root document for deserializing prompts.yaml.
/// Prompts are a dictionary keyed by prompt name.
/// </summary>
public class PromptsDocument
{
    public string Version { get; set; } = string.Empty;
    public Dictionary<string, PromptDefinition> Prompts { get; set; } = new();
}

/// <summary>
/// Represents a prompt definition from prompts.yaml.
/// Properties: key (dictionary key), description, template.
/// </summary>
public class PromptDefinition
{
    /// <summary>
    /// Populated from the dictionary key during loading; not present in the YAML value.
    /// </summary>
    [YamlIgnore]
    public string Key { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
}
