namespace AutonomousSoftwareFactory.Tests;

using AutonomousSoftwareFactory.Yaml;

public class YamlConfigLoaderTests
{
    private readonly YamlConfigLoader _loader = new();

    private static string ConfigPath(string fileName) =>
        Path.Combine(FindConfigsDir(), fileName);

    private static string FindConfigsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "configs");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not find 'configs' directory.");
    }

    [Fact]
    public void LoadWorkflow_DeserializesStepsAndMetadata()
    {
        var workflow = _loader.LoadWorkflow(ConfigPath("workflow.yaml"));

        Assert.Equal("AutonomousSoftwareFactoryWorkflow", workflow.Name);
        Assert.Equal("sequential", workflow.Execution.Mode);
        Assert.True(workflow.Execution.PersistState);
        Assert.True(workflow.Execution.EnableCheckpoints);
        Assert.Contains("requirement", workflow.Context.Inputs);
        Assert.Contains("project_context", workflow.Context.SharedState);
        Assert.Equal(2, workflow.Policies.Retry.MaxAttempts);
        Assert.True(workflow.Policies.Validation.RequireNonEmptyOutputs);
        Assert.True(workflow.Policies.Security.AllowGitWrite);

        Assert.True(workflow.Steps.Count >= 16);

        var firstStep = workflow.Steps[0];
        Assert.Equal("receive_requirement", firstStep.Id);
        Assert.Equal("input", firstStep.Type);
        Assert.Equal("analyze_project", firstStep.Next);
        Assert.NotNull(firstStep.Validations);
        Assert.Equal(2, firstStep.Validations!.Count);

        var loadCodebase = workflow.Steps.First(s => s.Id == "load_codebase");
        Assert.Equal("CodebaseLoaderAgent", loadCodebase.Agent);
        Assert.NotNull(loadCodebase.Retry);
        Assert.Equal(3, loadCodebase.Retry!.MaxAttempts);
        Assert.NotNull(loadCodebase.OnFailure);
        Assert.Equal("stop", loadCodebase.OnFailure!.Action);

        var lastStep = workflow.Steps.Last();
        Assert.Equal("finalize_workflow", lastStep.Id);
        Assert.Equal("output", lastStep.Type);
    }

    [Fact]
    public void LoadAgents_DeserializesAllAgents()
    {
        var agents = _loader.LoadAgents(ConfigPath("agents.yaml"));

        Assert.True(agents.Count >= 12);

        var supervisor = agents.First(a => a.Name == "SupervisorAgent");
        Assert.Equal("reserved", supervisor.Status);
        Assert.Contains("decision_making", supervisor.Skills);
        Assert.NotEmpty(supervisor.Prompt);

        var developer = agents.First(a => a.Name == "DeveloperAgent");
        Assert.Contains("coding", developer.Skills);
        Assert.Contains("write_file", developer.Tools);
        Assert.Contains("validated_specifications", developer.Input);
        Assert.Contains("implementation_result", developer.Output);
    }

    [Fact]
    public void LoadSkills_DeserializesAllSkills()
    {
        var skills = _loader.LoadSkills(ConfigPath("skills_registry.yaml"));

        Assert.True(skills.Count >= 11);

        var cognitive = skills.First(s => s.Name == "code_analysis");
        Assert.Equal("cognitive", cognitive.Type);
        Assert.NotEmpty(cognitive.Instructions);
        Assert.NotEmpty(cognitive.Constraints);
        Assert.Null(cognitive.Tools);

        var operational = skills.First(s => s.Name == "git_operations");
        Assert.Equal("operational", operational.Type);
        Assert.NotNull(operational.Tools);
        Assert.Contains("git_clone", operational.Tools!);
    }

    [Fact]
    public void LoadTools_DeserializesAllTools()
    {
        var tools = _loader.LoadTools(ConfigPath("tools.yaml"));

        Assert.True(tools.Count >= 15);

        var gitClone = tools.First(t => t.Name == "git_clone");
        Assert.Equal("git", gitClone.Category);
        Assert.Equal("command", gitClone.ExecutionType);
        Assert.Contains("repo_url", gitClone.Input.Keys);
        Assert.NotNull(gitClone.Command);

        var createPr = tools.First(t => t.Name == "create_pull_request");
        Assert.Equal("api", createPr.ExecutionType);
        Assert.NotNull(createPr.Api);
        Assert.Equal("github", createPr.Api!.Provider);
        Assert.Equal("POST", createPr.Api.Method);

        var writeFile = tools.First(t => t.Name == "write_file");
        Assert.Equal("internal", writeFile.ExecutionType);
        Assert.NotNull(writeFile.Constraints);
    }

    [Fact]
    public void LoadPrompts_DeserializesAllPrompts()
    {
        var prompts = _loader.LoadPrompts(ConfigPath("prompts.yaml"));

        Assert.True(prompts.Count >= 7);

        var system = prompts.First(p => p.Key == "system");
        Assert.NotEmpty(system.Description);
        Assert.NotEmpty(system.Template);

        var prTemplate = prompts.First(p => p.Key == "pr_template");
        Assert.Contains("O que foi feito", prTemplate.Template);

        // All prompts should have their Key populated
        Assert.All(prompts, p => Assert.NotEmpty(p.Key));
    }
}