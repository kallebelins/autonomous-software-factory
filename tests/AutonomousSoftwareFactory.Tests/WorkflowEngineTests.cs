namespace AutonomousSoftwareFactory.Tests;

using AutonomousSoftwareFactory.Agents;
using AutonomousSoftwareFactory.Models;
using AutonomousSoftwareFactory.State;
using AutonomousSoftwareFactory.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class WorkflowEngineTests
{
    private readonly IStateStore _stateStore = new InMemoryStateStore();
    private readonly ILogger<WorkflowEngine> _logger = NullLoggerFactory.Instance.CreateLogger<WorkflowEngine>();

    private static WorkflowDefinition MinimalWorkflow(params StepDefinition[] steps) => new()
    {
        Name = "TestWorkflow",
        Description = "Test",
        Execution = new ExecutionSettings { Mode = "sequential" },
        Context = new ContextDefinition(),
        Policies = new PoliciesDefinition
        {
            Retry = new RetryPolicy { MaxAttempts = 1 },
            Validation = new ValidationPolicy(),
            Security = new SecurityPolicy()
        },
        Steps = steps.ToList()
    };

    private WorkflowEngine CreateEngine(
        WorkflowDefinition workflow,
        IAgentExecutor? agentExecutor = null,
        List<AgentDefinition>? agents = null,
        List<PromptDefinition>? prompts = null) =>
        new(
            workflow,
            agents ?? [],
            [],
            [],
            prompts ?? [],
            agentExecutor ?? new SuccessAgentExecutor(),
            _stateStore,
            _logger);

    // --- Input + Output step tests ---

    [Fact]
    public async Task Execute_InputAndOutputSteps_CompletesSuccessfully()
    {
        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "input_step",
                Name = "Receive Input",
                Type = "input",
                Input = new() { ["requirement"] = "{{context.inputs.requirement}}" },
                Output = new() { ["requirement"] = "requirement" },
                Next = "output_step"
            },
            new StepDefinition
            {
                Id = "output_step",
                Name = "Final Output",
                Type = "output",
                Input = new() { ["result"] = "{{steps.input_step.output.requirement}}" },
                Output = new() { ["result"] = "result" }
            });

        var engine = CreateEngine(workflow);
        var context = new ExecutionContext
        {
            Inputs = new() { ["requirement"] = "Build a REST API" }
        };

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Empty(result.Errors);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task Execute_InputStep_SavesOutputToStateStore()
    {
        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "input_step",
                Name = "Receive Input",
                Type = "input",
                Input = new() { ["requirement"] = "{{context.inputs.requirement}}" },
                Output = new() { ["requirement"] = "requirement" }
            });

        var engine = CreateEngine(workflow);
        var context = new ExecutionContext
        {
            Inputs = new() { ["requirement"] = "Some requirement" }
        };

        await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.True(_stateStore.Has("steps.input_step.output.requirement"));
        Assert.Equal("Some requirement", _stateStore.Get<string>("steps.input_step.output.requirement"));
    }

    [Fact]
    public async Task Execute_InputStep_ValidationRequired_FailsWhenFieldMissing()
    {
        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "input_step",
                Name = "Receive Input",
                Type = "input",
                Input = new(), // no "requirement" mapping — validation will fail
                Output = new() { ["requirement"] = "requirement" },
                Validations = [new StepValidation { Field = "requirement", Rule = "required" }]
            });

        var engine = CreateEngine(workflow);
        var context = new ExecutionContext();

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Execute_InputStep_ValidationRequired_PassesWhenFieldPresent()
    {
        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "input_step",
                Name = "Receive Input",
                Type = "input",
                Input = new() { ["requirement"] = "{{context.inputs.requirement}}" },
                Output = new() { ["requirement"] = "requirement" },
                Validations = [new StepValidation { Field = "requirement", Rule = "required" }]
            });

        var engine = CreateEngine(workflow);
        var context = new ExecutionContext
        {
            Inputs = new() { ["requirement"] = "Valid requirement" }
        };

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Empty(result.Errors);
    }

    // --- Template resolution tests ---

    [Fact]
    public async Task Execute_ResolvesTemplateExpressions_AcrossSteps()
    {
        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "step1",
                Name = "Step 1",
                Type = "input",
                Input = new() { ["data"] = "{{context.inputs.data}}" },
                Output = new() { ["data"] = "data" },
                Next = "step2"
            },
            new StepDefinition
            {
                Id = "step2",
                Name = "Step 2",
                Type = "input",
                Input = new() { ["prev_data"] = "{{steps.step1.output.data}}" },
                Output = new() { ["prev_data"] = "prev_data" }
            });

        var engine = CreateEngine(workflow);
        var context = new ExecutionContext
        {
            Inputs = new() { ["data"] = "hello" }
        };

        await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("hello", _stateStore.Get<string>("steps.step2.output.prev_data"));
    }

    // --- Agent step tests ---

    [Fact]
    public async Task Execute_AgentStep_CallsAgentExecutorAndSavesOutput()
    {
        var agentExecutor = new SuccessAgentExecutor(new Dictionary<string, object>
        {
            ["analysis_result"] = "Project uses .NET 8"
        });

        var agents = new List<AgentDefinition>
        {
            new()
            {
                Name = "TestAgent",
                Skills = [],
                Tools = [],
                Prompt = "test_prompt"
            }
        };

        var prompts = new List<PromptDefinition>
        {
            new() { Key = "test_prompt", Template = "Analyze this." }
        };

        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "agent_step",
                Name = "Agent Step",
                Type = "agent",
                Agent = "TestAgent",
                Input = new() { ["requirement"] = "{{context.inputs.requirement}}" },
                Output = new() { ["analysis_result"] = "analysis_result" }
            });

        var engine = CreateEngine(workflow, agentExecutor, agents, prompts);
        var context = new ExecutionContext
        {
            Inputs = new() { ["requirement"] = "Build API" }
        };

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("Project uses .NET 8", _stateStore.Get<string>("steps.agent_step.output.analysis_result"));
    }

    [Fact]
    public async Task Execute_AgentStep_AgentNotFound_FailsStep()
    {
        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "agent_step",
                Name = "Agent Step",
                Type = "agent",
                Agent = "NonExistentAgent",
                Input = new(),
                Output = new()
            });

        var engine = CreateEngine(workflow, agents: []);

        var result = await engine.ExecuteAsync(new ExecutionContext(), CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.NotEmpty(result.Errors);
    }

    // --- Retry tests ---

    [Fact]
    public async Task Execute_RetryPolicy_RetriesOnFailure()
    {
        var agentExecutor = new FailThenSucceedAgentExecutor(failCount: 1);

        var agents = new List<AgentDefinition>
        {
            new() { Name = "RetryAgent", Skills = [], Tools = [], Prompt = "p" }
        };

        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "retry_step",
                Name = "Retry Step",
                Type = "agent",
                Agent = "RetryAgent",
                Input = new(),
                Output = new(),
                Retry = new RetryPolicy { MaxAttempts = 3 }
            });

        var engine = CreateEngine(workflow, agentExecutor, agents);

        var result = await engine.ExecuteAsync(new ExecutionContext(), CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal(2, agentExecutor.CallCount);
    }

    [Fact]
    public async Task Execute_RetryPolicy_ExhaustsRetries_Fails()
    {
        var agentExecutor = new AlwaysFailAgentExecutor();

        var agents = new List<AgentDefinition>
        {
            new() { Name = "FailAgent", Skills = [], Tools = [], Prompt = "p" }
        };

        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "fail_step",
                Name = "Fail Step",
                Type = "agent",
                Agent = "FailAgent",
                Input = new(),
                Output = new(),
                Retry = new RetryPolicy { MaxAttempts = 2 }
            });

        var engine = CreateEngine(workflow, agentExecutor, agents);

        var result = await engine.ExecuteAsync(new ExecutionContext(), CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal(2, agentExecutor.CallCount);
    }

    // --- OnFailure policy tests ---

    [Fact]
    public async Task Execute_OnFailureContinue_ContinuesToNextStep()
    {
        var agentExecutor = new AlwaysFailAgentExecutor();

        var agents = new List<AgentDefinition>
        {
            new() { Name = "FailAgent", Skills = [], Tools = [], Prompt = "p" }
        };

        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "fail_step",
                Name = "Fail Step",
                Type = "agent",
                Agent = "FailAgent",
                Input = new(),
                Output = new(),
                Retry = new RetryPolicy { MaxAttempts = 1 },
                OnFailure = new OnFailureDefinition { Action = "continue", Message = "Skipping" },
                Next = "final_step"
            },
            new StepDefinition
            {
                Id = "final_step",
                Name = "Final",
                Type = "output",
                Input = new(),
                Output = new()
            });

        var engine = CreateEngine(workflow, agentExecutor, agents);

        var result = await engine.ExecuteAsync(new ExecutionContext(), CancellationToken.None);

        Assert.Equal("completed", result.Status);
    }

    [Fact]
    public async Task Execute_OnFailureStop_StopsWorkflow()
    {
        var agentExecutor = new AlwaysFailAgentExecutor();

        var agents = new List<AgentDefinition>
        {
            new() { Name = "FailAgent", Skills = [], Tools = [], Prompt = "p" }
        };

        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "fail_step",
                Name = "Fail Step",
                Type = "agent",
                Agent = "FailAgent",
                Input = new(),
                Output = new(),
                Retry = new RetryPolicy { MaxAttempts = 1 },
                OnFailure = new OnFailureDefinition { Action = "stop", Message = "Critical failure" },
                Next = "should_not_reach"
            },
            new StepDefinition
            {
                Id = "should_not_reach",
                Name = "Unreachable",
                Type = "output",
                Input = new(),
                Output = new()
            });

        var engine = CreateEngine(workflow, agentExecutor, agents);

        var result = await engine.ExecuteAsync(new ExecutionContext(), CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.False(_stateStore.Has("steps.should_not_reach.output"));
    }

    // --- Cancellation test ---

    [Fact]
    public async Task Execute_CancellationRequested_ThrowsOperationCanceledException()
    {
        var workflow = MinimalWorkflow(
            new StepDefinition
            {
                Id = "step1",
                Name = "Step",
                Type = "input",
                Input = new(),
                Output = new()
            });

        var engine = CreateEngine(workflow);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.ExecuteAsync(new ExecutionContext(), cts.Token));
    }

    // --- Test doubles ---

    private class SuccessAgentExecutor : IAgentExecutor
    {
        private readonly Dictionary<string, object> _data;

        public SuccessAgentExecutor(Dictionary<string, object>? data = null)
        {
            _data = data ?? new();
        }

        public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct)
        {
            return Task.FromResult(new AgentResult
            {
                Status = "success",
                Data = new Dictionary<string, object>(_data),
                Message = "OK"
            });
        }
    }

    private class AlwaysFailAgentExecutor : IAgentExecutor
    {
        public int CallCount { get; private set; }

        public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new AgentResult
            {
                Status = "error",
                Message = "Simulated failure"
            });
        }
    }

    private class FailThenSucceedAgentExecutor : IAgentExecutor
    {
        private readonly int _failCount;
        public int CallCount { get; private set; }

        public FailThenSucceedAgentExecutor(int failCount)
        {
            _failCount = failCount;
        }

        public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct)
        {
            CallCount++;
            var status = CallCount <= _failCount ? "error" : "success";
            return Task.FromResult(new AgentResult
            {
                Status = status,
                Message = status == "success" ? "OK" : "Fail"
            });
        }
    }
}
