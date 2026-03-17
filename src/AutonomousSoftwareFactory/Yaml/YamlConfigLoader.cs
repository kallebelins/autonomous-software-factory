namespace AutonomousSoftwareFactory.Yaml;

using AutonomousSoftwareFactory.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class YamlConfigLoader : IYamlConfigLoader
{
    private readonly IDeserializer _deserializer;

    public YamlConfigLoader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public WorkflowDefinition LoadWorkflow(string path)
    {
        var yaml = File.ReadAllText(path);
        var document = _deserializer.Deserialize<WorkflowDocument>(yaml);
        return document.Workflow;
    }

    public List<AgentDefinition> LoadAgents(string path)
    {
        var yaml = File.ReadAllText(path);
        var document = _deserializer.Deserialize<AgentsDocument>(yaml);
        return document.Agents;
    }

    public List<SkillDefinition> LoadSkills(string path)
    {
        var yaml = File.ReadAllText(path);
        var document = _deserializer.Deserialize<SkillsDocument>(yaml);
        return document.Skills;
    }

    public List<ToolDefinition> LoadTools(string path)
    {
        var yaml = File.ReadAllText(path);
        var document = _deserializer.Deserialize<ToolsDocument>(yaml);
        return document.Tools;
    }

    public List<PromptDefinition> LoadPrompts(string path)
    {
        var yaml = File.ReadAllText(path);
        var document = _deserializer.Deserialize<PromptsDocument>(yaml);

        var prompts = new List<PromptDefinition>();
        foreach (var (key, prompt) in document.Prompts)
        {
            prompt.Key = key;
            prompts.Add(prompt);
        }

        return prompts;
    }
}
