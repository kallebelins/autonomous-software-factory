namespace AutonomousSoftwareFactory.Yaml;

using AutonomousSoftwareFactory.Models;

public interface IYamlConfigLoader
{
    WorkflowDefinition LoadWorkflow(string path);
    List<AgentDefinition> LoadAgents(string path);
    List<SkillDefinition> LoadSkills(string path);
    List<ToolDefinition> LoadTools(string path);
    List<PromptDefinition> LoadPrompts(string path);
}
