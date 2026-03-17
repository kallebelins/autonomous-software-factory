using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AutonomousSoftwareFactory.Agents;
using AutonomousSoftwareFactory.Llm;
using AutonomousSoftwareFactory.Models;
using AutonomousSoftwareFactory.State;
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
services.AddSingleton<IAgentExecutor, StubAgentExecutor>();
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

services.AddSingleton<IWorkflowEngine, WorkflowEngine>();

var serviceProvider = services.BuildServiceProvider();

// 4. Parse --requirement argument
var requirementPath = ParseRequirementArg(args);
if (requirementPath is null)
{
    Console.Error.WriteLine("Usage: AutonomousSoftwareFactory --requirement <path-to-requirement.json>");
    return 1;
}

if (!File.Exists(requirementPath))
{
    Console.Error.WriteLine($"Requirement file not found: {requirementPath}");
    return 1;
}

// 5. Create ExecutionContext with inputs from requirement JSON
var requirementJson = File.ReadAllText(requirementPath);
var requirementData = JsonSerializer.Deserialize<Dictionary<string, object>>(requirementJson)
    ?? new Dictionary<string, object>();

var context = new AutonomousSoftwareFactory.Models.ExecutionContext
{
    Inputs = requirementData
};

// 6. Execute workflow
var engine = serviceProvider.GetRequiredService<IWorkflowEngine>();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Starting workflow execution...");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var result = await engine.ExecuteAsync(context, cts.Token);

// 7. Display final result
Console.WriteLine();
Console.WriteLine("=== Execution Result ===");
Console.WriteLine($"Status:   {result.Status}");
Console.WriteLine($"Duration: {result.Duration}");

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

static string? ParseRequirementArg(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--requirement")
            return args[i + 1];
    }

    return null;
}
