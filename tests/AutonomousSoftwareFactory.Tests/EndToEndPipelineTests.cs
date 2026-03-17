namespace AutonomousSoftwareFactory.Tests;

using System.Text.Json;
using AutonomousSoftwareFactory.Agents;
using AutonomousSoftwareFactory.Models;
using AutonomousSoftwareFactory.State;
using AutonomousSoftwareFactory.Workflow;
using AutonomousSoftwareFactory.Yaml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// End-to-end pipeline tests that load real YAML configs from configs/ and
/// validate the full 16-step workflow execution with mock agent executor.
/// </summary>
public class EndToEndPipelineTests
{
    private static readonly string[] ExpectedStepOrder =
    [
        "receive_requirement",
        "analyze_project",
        "load_codebase",
        "analyze_codebase",
        "generate_backlog",
        "breakdown_tasks",
        "generate_specs",
        "validate_architecture",
        "setup_environment",
        "implement_code",
        "execute_build",
        "execute_tests",
        "validate_quality",
        "review_delivery",
        "create_pull_request",
        "finalize_workflow"
    ];

    private readonly YamlConfigLoader _loader = new();

    private static string ConfigPath(string fileName) =>
        Path.Combine(FindConfigsDir(), fileName);

    private static string SamplePath(string fileName) =>
        Path.Combine(FindSamplesDir(), fileName);

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

    private static string FindSamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "samples");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not find 'samples' directory.");
    }

    private (WorkflowDefinition workflow, List<AgentDefinition> agents,
        List<SkillDefinition> skills, List<ToolDefinition> tools,
        List<PromptDefinition> prompts) LoadAllConfigs()
    {
        var workflow = _loader.LoadWorkflow(ConfigPath("workflow.yaml"));
        var agents = _loader.LoadAgents(ConfigPath("agents.yaml"));
        var skills = _loader.LoadSkills(ConfigPath("skills_registry.yaml"));
        var tools = _loader.LoadTools(ConfigPath("tools.yaml"));
        var prompts = _loader.LoadPrompts(ConfigPath("prompts.yaml"));
        return (workflow, agents, skills, tools, prompts);
    }

    private static ExecutionContext CreateContextFromSamples()
    {
        var requirementJson = File.ReadAllText(SamplePath("requirement-sample.json"));
        var metadataJson = File.ReadAllText(SamplePath("project-metadata-sample.json"));

        var requirement = JsonSerializer.Deserialize<Dictionary<string, object>>(requirementJson)!;
        var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson)!;

        return new ExecutionContext
        {
            Inputs = new Dictionary<string, object>
            {
                ["requirement"] = requirement,
                ["repository"] = requirement.TryGetValue("repository", out var repo) ? repo : new Dictionary<string, object>(),
                ["project_metadata"] = metadata,
                ["execution_settings"] = new Dictionary<string, object>
                {
                    ["max_retries"] = 2,
                    ["log_level"] = "info"
                }
            }
        };
    }

    // ────────────────── Test 1: Full 16-step sequential execution ──────────────────

    [Fact]
    public async Task FullPipeline_ExecutesAll16Steps_InCorrectOrder()
    {
        var (workflow, agents, skills, tools, prompts) = LoadAllConfigs();
        var stateStore = new InMemoryStateStore();
        var tracker = new TrackingAgentExecutor();

        var engine = new WorkflowEngine(
            workflow, agents, skills, tools, prompts,
            tracker, stateStore,
            NullLoggerFactory.Instance.CreateLogger<WorkflowEngine>());

        var context = CreateContextFromSamples();

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Empty(result.Errors);
        Assert.True(result.Duration > TimeSpan.Zero);

        // 14 agent steps (16 total minus receive_requirement[input] and finalize_workflow[output])
        Assert.Equal(14, tracker.ExecutedAgents.Count);

        // Verify agent execution order matches the expected workflow sequence
        var expectedAgentOrder = new[]
        {
            "ProjectAnalyzerAgent",
            "CodebaseLoaderAgent",
            "CodebaseAnalyzerAgent",
            "BacklogAgent",
            "TaskBreakdownAgent",
            "SpecAgent",
            "ArchitectureValidatorAgent",
            "EnvironmentSetupAgent",
            "DeveloperAgent",
            "BuildAgent",
            "TestAgent",
            "QualityAgent",
            "ReviewAgent",
            "PullRequestAgent"
        };

        Assert.Equal(expectedAgentOrder, tracker.ExecutedAgents);
    }

    // ────────────────── Test 2: Output chaining between steps ──────────────────

    [Fact]
    public async Task FullPipeline_OutputsFromEachStep_AreAvailableToNextSteps()
    {
        var (workflow, agents, skills, tools, prompts) = LoadAllConfigs();
        var stateStore = new InMemoryStateStore();
        var tracker = new TrackingAgentExecutor();

        var engine = new WorkflowEngine(
            workflow, agents, skills, tools, prompts,
            tracker, stateStore,
            NullLoggerFactory.Instance.CreateLogger<WorkflowEngine>());

        var context = CreateContextFromSamples();

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("completed", result.Status);

        // Verify input step saved its output
        Assert.True(stateStore.Has("steps.receive_requirement.output.validated_requirement"),
            "receive_requirement should save validated_requirement");

        // Verify each agent step saved its expected output key
        var expectedOutputs = new Dictionary<string, string>
        {
            ["analyze_project"] = "project_context",
            ["load_codebase"] = "codebase_path",
            ["analyze_codebase"] = "codebase_summary",
            ["generate_backlog"] = "features",
            ["breakdown_tasks"] = "microtasks",
            ["generate_specs"] = "specifications",
            ["validate_architecture"] = "validated_specifications",
            ["setup_environment"] = "environment_ready",
            ["implement_code"] = "implementation_result",
            ["execute_build"] = "build_result",
            ["execute_tests"] = "test_results",
            ["validate_quality"] = "quality_report",
            ["review_delivery"] = "review_status",
            ["create_pull_request"] = "pr_result"
        };

        foreach (var (stepId, outputKey) in expectedOutputs)
        {
            var stateKey = $"steps.{stepId}.output.{outputKey}";
            Assert.True(stateStore.Has(stateKey),
                $"Step '{stepId}' should have saved output '{outputKey}' in state store (key: {stateKey})");
        }

        // Verify chaining: analyze_project received requirement from receive_requirement
        var analyzeRequest = tracker.RequestsByAgent["ProjectAnalyzerAgent"];
        Assert.True(analyzeRequest.Inputs.ContainsKey("requirement"),
            "ProjectAnalyzerAgent should receive 'requirement' input from receive_requirement step");

        // Verify chaining: load_codebase received project_context from analyze_project
        var loadRequest = tracker.RequestsByAgent["CodebaseLoaderAgent"];
        Assert.True(loadRequest.Inputs.ContainsKey("project_context"),
            "CodebaseLoaderAgent should receive 'project_context' from analyze_project step");

        // Verify chaining: generate_backlog received data from multiple previous steps
        var backlogRequest = tracker.RequestsByAgent["BacklogAgent"];
        Assert.True(backlogRequest.Inputs.ContainsKey("requirement"),
            "BacklogAgent should receive 'requirement'");
        Assert.True(backlogRequest.Inputs.ContainsKey("project_context"),
            "BacklogAgent should receive 'project_context'");
        Assert.True(backlogRequest.Inputs.ContainsKey("codebase_summary"),
            "BacklogAgent should receive 'codebase_summary'");

        // Verify chaining: create_pull_request received data from multiple steps
        var prRequest = tracker.RequestsByAgent["PullRequestAgent"];
        Assert.True(prRequest.Inputs.ContainsKey("review_status"),
            "PullRequestAgent should receive 'review_status'");
        Assert.True(prRequest.Inputs.ContainsKey("codebase_path"),
            "PullRequestAgent should receive 'codebase_path'");
        Assert.True(prRequest.Inputs.ContainsKey("project_context"),
            "PullRequestAgent should receive 'project_context'");
        Assert.True(prRequest.Inputs.ContainsKey("requirement"),
            "PullRequestAgent should receive 'requirement'");
    }

    // ────────────────── Test 3: Retry works on steps with retry policy ──────────────────

    [Fact]
    public async Task FullPipeline_RetryOnAgentFailure_RetriesAndSucceeds()
    {
        var (workflow, agents, skills, tools, prompts) = LoadAllConfigs();
        var stateStore = new InMemoryStateStore();

        // CodebaseLoaderAgent fails once then succeeds (retry: max_attempts 3)
        var tracker = new TrackingAgentExecutor(failAgentsOnce: ["CodebaseLoaderAgent"]);

        var engine = new WorkflowEngine(
            workflow, agents, skills, tools, prompts,
            tracker, stateStore,
            NullLoggerFactory.Instance.CreateLogger<WorkflowEngine>());

        var context = CreateContextFromSamples();

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("completed", result.Status);

        // CodebaseLoaderAgent should have been called twice (fail + success)
        Assert.Equal(2, tracker.CallCountByAgent["CodebaseLoaderAgent"]);

        // All other agents should be called once
        Assert.Equal(1, tracker.CallCountByAgent["ProjectAnalyzerAgent"]);
        Assert.Equal(1, tracker.CallCountByAgent["BuildAgent"]);
    }

    // ────────────────── Test 4: on_failure continue allows workflow to proceed ──────────────────

    [Fact]
    public async Task FullPipeline_TestStepFailsWithContinue_WorkflowProceeds()
    {
        var (workflow, agents, skills, tools, prompts) = LoadAllConfigs();
        var stateStore = new InMemoryStateStore();

        // TestAgent always fails — execute_tests has on_failure: continue
        var tracker = new TrackingAgentExecutor(alwaysFailAgents: ["TestAgent"]);

        var engine = new WorkflowEngine(
            workflow, agents, skills, tools, prompts,
            tracker, stateStore,
            NullLoggerFactory.Instance.CreateLogger<WorkflowEngine>());

        var context = CreateContextFromSamples();

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        // Workflow should complete despite TestAgent failure (on_failure: continue)
        Assert.Equal("completed", result.Status);

        // TestAgent was called (max_attempts=2 for execute_tests step)
        Assert.True(tracker.CallCountByAgent.ContainsKey("TestAgent"));
        Assert.Equal(2, tracker.CallCountByAgent["TestAgent"]);

        // Steps after execute_tests should still have executed
        Assert.True(tracker.ExecutedAgents.Contains("QualityAgent"),
            "QualityAgent should execute after TestAgent failure with on_failure: continue");
        Assert.True(tracker.ExecutedAgents.Contains("ReviewAgent"),
            "ReviewAgent should execute after TestAgent failure");
        Assert.True(tracker.ExecutedAgents.Contains("PullRequestAgent"),
            "PullRequestAgent should execute after TestAgent failure");
    }

    // ────────────────── Test 5: on_failure stop halts the workflow ──────────────────

    [Fact]
    public async Task FullPipeline_BuildStepFailsWithStop_WorkflowStops()
    {
        var (workflow, agents, skills, tools, prompts) = LoadAllConfigs();
        var stateStore = new InMemoryStateStore();

        // BuildAgent always fails — execute_build has on_failure: stop
        var tracker = new TrackingAgentExecutor(alwaysFailAgents: ["BuildAgent"]);

        var engine = new WorkflowEngine(
            workflow, agents, skills, tools, prompts,
            tracker, stateStore,
            NullLoggerFactory.Instance.CreateLogger<WorkflowEngine>());

        var context = CreateContextFromSamples();

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.NotEmpty(result.Errors);

        // Steps after execute_build should NOT have executed
        Assert.DoesNotContain("TestAgent", tracker.ExecutedAgents);
        Assert.DoesNotContain("QualityAgent", tracker.ExecutedAgents);
        Assert.DoesNotContain("ReviewAgent", tracker.ExecutedAgents);
        Assert.DoesNotContain("PullRequestAgent", tracker.ExecutedAgents);
    }

    // ────────────────── Test 6: PR creation agent receives correct data ──────────────────

    [Fact]
    public async Task FullPipeline_PullRequestAgent_ReceivesReviewStatusAndContext()
    {
        var (workflow, agents, skills, tools, prompts) = LoadAllConfigs();
        var stateStore = new InMemoryStateStore();
        var tracker = new TrackingAgentExecutor();

        var engine = new WorkflowEngine(
            workflow, agents, skills, tools, prompts,
            tracker, stateStore,
            NullLoggerFactory.Instance.CreateLogger<WorkflowEngine>());

        var context = CreateContextFromSamples();

        await engine.ExecuteAsync(context, CancellationToken.None);

        var prRequest = tracker.RequestsByAgent["PullRequestAgent"];

        // Verify review_status was resolved from review_delivery step output
        Assert.NotNull(prRequest.Inputs["review_status"]);

        // Verify codebase_path was resolved from load_codebase step output
        Assert.NotNull(prRequest.Inputs["codebase_path"]);

        // Verify pr_result is saved in state store
        Assert.True(stateStore.Has("steps.create_pull_request.output.pr_result"),
            "PR result should be saved in state store");
    }

    // ────────────────── Test 7: Multiple retries on different steps ──────────────────

    [Fact]
    public async Task FullPipeline_MultipleStepsWithRetry_AllRecoverSuccessfully()
    {
        var (workflow, agents, skills, tools, prompts) = LoadAllConfigs();
        var stateStore = new InMemoryStateStore();

        // Multiple agents fail once: CodebaseLoaderAgent (max 3), BuildAgent (max 3), PullRequestAgent (max 2)
        var tracker = new TrackingAgentExecutor(failAgentsOnce:
            ["CodebaseLoaderAgent", "BuildAgent", "PullRequestAgent"]);

        var engine = new WorkflowEngine(
            workflow, agents, skills, tools, prompts,
            tracker, stateStore,
            NullLoggerFactory.Instance.CreateLogger<WorkflowEngine>());

        var context = CreateContextFromSamples();

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("completed", result.Status);

        // Each failing agent should be retried once
        Assert.Equal(2, tracker.CallCountByAgent["CodebaseLoaderAgent"]);
        Assert.Equal(2, tracker.CallCountByAgent["BuildAgent"]);
        Assert.Equal(2, tracker.CallCountByAgent["PullRequestAgent"]);
    }

    // ────────────────── Test 8: Finalize step consolidates all outputs ──────────────────

    [Fact]
    public async Task FullPipeline_FinalizeStep_ConsolidatesOutputs()
    {
        var (workflow, agents, skills, tools, prompts) = LoadAllConfigs();
        var stateStore = new InMemoryStateStore();
        var tracker = new TrackingAgentExecutor();

        var engine = new WorkflowEngine(
            workflow, agents, skills, tools, prompts,
            tracker, stateStore,
            NullLoggerFactory.Instance.CreateLogger<WorkflowEngine>());

        var context = CreateContextFromSamples();

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("completed", result.Status);

        // finalize_workflow is an output step — it should have resolved inputs from all prior steps
        // The step collects: project_context, codebase_summary, features, microtasks,
        // specifications, validated_specifications, build_result, test_results,
        // quality_report, review_status, pr_result
        Assert.True(stateStore.Has("steps.finalize_workflow.output.final_status"),
            "finalize_workflow should save final_status output");
    }

    // ──────────────────────────── Test doubles ────────────────────────────

    /// <summary>
    /// Mock agent executor that tracks executed agents, records requests,
    /// and returns appropriate data per agent name. Supports simulating
    /// failures for specific agents.
    /// </summary>
    private class TrackingAgentExecutor : IAgentExecutor
    {
        private readonly HashSet<string> _failAgentsOnce;
        private readonly HashSet<string> _alwaysFailAgents;
        private readonly HashSet<string> _alreadyFailed = new(StringComparer.OrdinalIgnoreCase);

        public List<string> ExecutedAgents { get; } = [];
        public Dictionary<string, AgentExecutionRequest> RequestsByAgent { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> CallCountByAgent { get; } = new(StringComparer.OrdinalIgnoreCase);

        public TrackingAgentExecutor(
            IEnumerable<string>? failAgentsOnce = null,
            IEnumerable<string>? alwaysFailAgents = null)
        {
            _failAgentsOnce = new HashSet<string>(
                failAgentsOnce ?? [], StringComparer.OrdinalIgnoreCase);
            _alwaysFailAgents = new HashSet<string>(
                alwaysFailAgents ?? [], StringComparer.OrdinalIgnoreCase);
        }

        public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct)
        {
            var agentName = request.Agent.Name;

            CallCountByAgent.TryGetValue(agentName, out var count);
            CallCountByAgent[agentName] = count + 1;

            // Check if this agent should always fail
            if (_alwaysFailAgents.Contains(agentName))
            {
                return Task.FromResult(new AgentResult
                {
                    Status = "error",
                    Message = $"Simulated persistent failure for {agentName}"
                });
            }

            // Check if this agent should fail once
            if (_failAgentsOnce.Contains(agentName) && !_alreadyFailed.Contains(agentName))
            {
                _alreadyFailed.Add(agentName);
                return Task.FromResult(new AgentResult
                {
                    Status = "error",
                    Message = $"Simulated transient failure for {agentName}"
                });
            }

            ExecutedAgents.Add(agentName);
            RequestsByAgent[agentName] = request;

            var data = GetAgentResponseData(agentName);

            return Task.FromResult(new AgentResult
            {
                Status = "success",
                Data = data,
                Message = $"{agentName} completed successfully"
            });
        }

        private static Dictionary<string, object> GetAgentResponseData(string agentName)
        {
            return agentName switch
            {
                "ProjectAnalyzerAgent" => new()
                {
                    ["project_context"] = new Dictionary<string, object>
                    {
                        ["stack"] = "dotnet",
                        ["language"] = "csharp",
                        ["framework"] = "aspnet",
                        ["architecture"] = "clean-architecture"
                    }
                },
                "CodebaseLoaderAgent" => new()
                {
                    ["codebase_path"] = "/workspace/repos/project-name"
                },
                "CodebaseAnalyzerAgent" => new()
                {
                    ["codebase_summary"] = new Dictionary<string, object>
                    {
                        ["modules"] = new[] { "Controllers", "Services", "Models" },
                        ["patterns"] = new[] { "Repository", "Dependency Injection" },
                        ["dependencies"] = new[] { "EntityFramework", "FluentValidation" }
                    }
                },
                "BacklogAgent" => new()
                {
                    ["features"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["title"] = "User Registration Endpoint",
                            ["description"] = "POST /api/users",
                            ["priority"] = "high"
                        }
                    }
                },
                "TaskBreakdownAgent" => new()
                {
                    ["microtasks"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["title"] = "Create UserController",
                            ["type"] = "implementation"
                        },
                        new Dictionary<string, object>
                        {
                            ["title"] = "Add email validation",
                            ["type"] = "implementation"
                        }
                    }
                },
                "SpecAgent" => new()
                {
                    ["specifications"] = new Dictionary<string, object>
                    {
                        ["plan"] = "Implement user registration with validation",
                        ["files"] = new[] { "UserController.cs", "UserService.cs", "UserDto.cs" }
                    }
                },
                "ArchitectureValidatorAgent" => new()
                {
                    ["validated_specifications"] = new Dictionary<string, object>
                    {
                        ["approved"] = true,
                        ["notes"] = "Architecture is consistent"
                    }
                },
                "EnvironmentSetupAgent" => new()
                {
                    ["environment_ready"] = true
                },
                "DeveloperAgent" => new()
                {
                    ["implementation_result"] = new Dictionary<string, object>
                    {
                        ["files_created"] = 3,
                        ["files_modified"] = 1,
                        ["status"] = "implemented"
                    }
                },
                "BuildAgent" => new()
                {
                    ["build_result"] = new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["warnings"] = 0,
                        ["errors"] = 0
                    }
                },
                "TestAgent" => new()
                {
                    ["test_results"] = new Dictionary<string, object>
                    {
                        ["total"] = 5,
                        ["passed"] = 5,
                        ["failed"] = 0
                    }
                },
                "QualityAgent" => new()
                {
                    ["quality_report"] = new Dictionary<string, object>
                    {
                        ["issues"] = 0,
                        ["score"] = "A"
                    }
                },
                "ReviewAgent" => new()
                {
                    ["review_status"] = new Dictionary<string, object>
                    {
                        ["approved"] = true,
                        ["comments"] = "Ready for delivery"
                    }
                },
                "PullRequestAgent" => new()
                {
                    ["pr_result"] = new Dictionary<string, object>
                    {
                        ["pr_number"] = 42,
                        ["url"] = "https://github.com/owner/project-name/pull/42",
                        ["branch"] = "feature/user-registration"
                    }
                },
                _ => new()
            };
        }
    }
}
