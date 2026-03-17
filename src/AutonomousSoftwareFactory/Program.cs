using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AutonomousSoftwareFactory.Agents;
using AutonomousSoftwareFactory.Llm;
using AutonomousSoftwareFactory.Logging;
using AutonomousSoftwareFactory.Models;
using AutonomousSoftwareFactory.State;
using AutonomousSoftwareFactory.Tools;
using AutonomousSoftwareFactory.Workflow;
using AutonomousSoftwareFactory.Yaml;

// 1. Configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

// 2. DI — register services
var services = new ServiceCollection();

services.AddSingleton<IConfiguration>(configuration);

services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

services.AddSingleton<IYamlConfigLoader, YamlConfigLoader>();
services.AddSingleton<IStateStore, InMemoryStateStore>();
services.AddHttpClient();
services.AddSingleton<IToolExecutor, ToolExecutor>();
services.AddSingleton<IAgentExecutor, AgentExecutor>();
services.AddHttpClient<ILlmClient, LlmClient>();

// 3. Load YAML configs
var configsPath = configuration["Configs:BasePath"] ?? "./configs";

var configLoader = new YamlConfigLoader();
var workflow = configLoader.LoadWorkflow(Path.Combine(configsPath, "workflow.yaml"));
var agents = configLoader.LoadAgents(Path.Combine(configsPath, "agents.yaml"));
var skills = configLoader.LoadSkills(Path.Combine(configsPath, "skills_registry.yaml"));
var tools = configLoader.LoadTools(Path.Combine(configsPath, "tools.yaml"));
var prompts = configLoader.LoadPrompts(Path.Combine(configsPath, "prompts.yaml"));

services.AddSingleton(workflow);
services.AddSingleton(agents);
services.AddSingleton(skills);
services.AddSingleton(tools);
services.AddSingleton(prompts);

// 3.5. Run logger — per-run file log
var logPath = configuration["Execution:LogPath"] ?? "./logs";
var runLogger = new FileRunLogger(logPath, workflow.Name);
services.AddSingleton<IRunLogger>(runLogger);

// 3.6. Checkpoint directory
var checkpointPath = configuration["Execution:CheckpointPath"] ?? "./workspace/checkpoints";
services.AddSingleton<IWorkflowEngine>(sp => new WorkflowEngine(
    sp.GetRequiredService<WorkflowDefinition>(),
    sp.GetRequiredService<List<AgentDefinition>>(),
    sp.GetRequiredService<List<SkillDefinition>>(),
    sp.GetRequiredService<List<ToolDefinition>>(),
    sp.GetRequiredService<List<PromptDefinition>>(),
    sp.GetRequiredService<IAgentExecutor>(),
    sp.GetRequiredService<IStateStore>(),
    sp.GetRequiredService<ILogger<WorkflowEngine>>(),
    sp.GetRequiredService<IRunLogger>(),
    checkpointPath));

var serviceProvider = services.BuildServiceProvider();

// 4. Parse arguments
var requirementPath = ParseArg(args, "--requirement");
var resumePath = ParseArg(args, "--resume");

if (requirementPath is null && resumePath is null)
{
    Console.Error.WriteLine("Usage: AutonomousSoftwareFactory --requirement <path-to-requirement.json>");
    Console.Error.WriteLine("       AutonomousSoftwareFactory --resume <path-to-checkpoint.json>");
    return 1;
}

var engine = serviceProvider.GetRequiredService<IWorkflowEngine>();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

ExecutionResult result;

if (resumePath is not null)
{
    // Resume from checkpoint
    if (!File.Exists(resumePath))
    {
        Console.Error.WriteLine($"Checkpoint file not found: {resumePath}");
        return 1;
    }

    logger.LogInformation("Resuming workflow from checkpoint: {Path}", resumePath);
    result = await engine.ResumeAsync(resumePath, cts.Token);
}
else
{
    // Normal execution
    if (!File.Exists(requirementPath))
    {
        Console.Error.WriteLine($"Requirement file not found: {requirementPath}");
        return 1;
    }

    var requirementJson = File.ReadAllText(requirementPath!);
    var requirementData = JsonSerializer.Deserialize<Dictionary<string, object>>(requirementJson)
        ?? new Dictionary<string, object>();

    var context = new AutonomousSoftwareFactory.Models.ExecutionContext
    {
        Inputs = requirementData
    };

    logger.LogInformation("Starting workflow execution...");
    result = await engine.ExecuteAsync(context, cts.Token);
}

// Dispose run logger to flush all entries
runLogger.Dispose();

// 7. Display final result
Console.WriteLine();
Console.WriteLine("=== Execution Result ===");
Console.WriteLine($"Status:   {result.Status}");
Console.WriteLine($"Duration: {result.Duration}");
Console.WriteLine($"Log:      {runLogger.FilePath}");

if (result.Outputs.Count > 0)
{
    Console.WriteLine("Outputs:");
    foreach (KeyValuePair<string, object> entry in result.Outputs)
        Console.WriteLine($"  {entry.Key}: {entry.Value}");
}

if (result.Errors.Count > 0)
{
    Console.WriteLine("Errors:");
    foreach (var error in result.Errors)
        Console.WriteLine($"  - {error}");
}

return result.Status == "completed" ? 0 : 1;

static string? ParseArg(string[] args, string flag)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == flag)
            return args[i + 1];
    }

    return null;
}
