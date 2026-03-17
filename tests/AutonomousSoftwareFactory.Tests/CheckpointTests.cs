namespace AutonomousSoftwareFactory.Tests;

using System.Text.Json;
using AutonomousSoftwareFactory.Agents;
using AutonomousSoftwareFactory.Models;
using AutonomousSoftwareFactory.State;
using AutonomousSoftwareFactory.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class CheckpointTests : IDisposable
{
    private readonly string _checkpointDir;
    private readonly ILogger<WorkflowEngine> _logger = NullLoggerFactory.Instance.CreateLogger<WorkflowEngine>();

    public CheckpointTests()
    {
        _checkpointDir = Path.Combine(Path.GetTempPath(), $"checkpoint-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_checkpointDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_checkpointDir))
            Directory.Delete(_checkpointDir, recursive: true);
    }

    private static WorkflowDefinition CreateWorkflow(params StepDefinition[] steps) => new()
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
        IAgentExecutor agentExecutor,
        IStateStore stateStore,
        string? checkpointDir = null,
        List<AgentDefinition>? agents = null) =>
        new(
            workflow, agents ?? [], [], [], [],
            agentExecutor,
            stateStore,
            _logger,
            checkpointDirectory: checkpointDir ?? _checkpointDir);

    private static List<AgentDefinition> MakeAgents(params string[] names) =>
        names.Select(n => new AgentDefinition { Name = n, Skills = [], Tools = [], Prompt = "p" }).ToList();

    private static StepDefinition InputStep(string id, string? next = null) => new()
    {
        Id = id,
        Name = $"Step {id}",
        Type = "input",
        Input = new() { ["data"] = "{{context.inputs.data}}" },
        Output = new() { ["data"] = "data" },
        Next = next
    };

    private static StepDefinition AgentStep(string id, string agentName, string? next = null) => new()
    {
        Id = id,
        Name = $"Step {id}",
        Type = "agent",
        Agent = agentName,
        Input = new(),
        Output = new() { ["result"] = "result" },
        Next = next
    };

    private static StepDefinition OutputStep(string id) => new()
    {
        Id = id,
        Name = $"Step {id}",
        Type = "output",
        Input = new(),
        Output = new() { ["final"] = "final" }
    };

    // ──────── Test 1: Checkpoint file is created after each step ────────

    [Fact]
    public async Task Execute_WithCheckpointDir_SavesCheckpointAfterEachStep()
    {
        var workflow = CreateWorkflow(
            InputStep("step1", next: "step2"),
            InputStep("step2", next: "step3"),
            InputStep("step3"));

        var stateStore = new InMemoryStateStore();
        var engine = CreateEngine(workflow, new SuccessAgent(), stateStore);
        var context = new ExecutionContext { Inputs = new() { ["data"] = "test" } };

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("completed", result.Status);

        // Checkpoint file should exist
        var checkpointFile = Path.Combine(_checkpointDir, "TestWorkflow-checkpoint.json");
        Assert.True(File.Exists(checkpointFile), "Checkpoint file should be created");

        // Checkpoint should contain all 3 completed steps
        var json = File.ReadAllText(checkpointFile);
        var checkpoint = JsonSerializer.Deserialize<CheckpointData>(json)!;
        Assert.Equal(3, checkpoint.CompletedSteps.Count);
        Assert.Contains("step1", checkpoint.CompletedSteps);
        Assert.Contains("step2", checkpoint.CompletedSteps);
        Assert.Contains("step3", checkpoint.CompletedSteps);
        Assert.Equal("step3", checkpoint.LastCompletedStep);
        Assert.Equal("TestWorkflow", checkpoint.WorkflowName);
    }

    // ──────── Test 2: Checkpoint saved on failure (stop) ────────

    [Fact]
    public async Task Execute_FailsAtStep_SavesCheckpointWithCompletedSteps()
    {
        // 7-step workflow: steps 1-4 input, step 5 agent (fails), steps 6-7 unreachable
        var workflow = CreateWorkflow(
            InputStep("step1", next: "step2"),
            InputStep("step2", next: "step3"),
            InputStep("step3", next: "step4"),
            InputStep("step4", next: "step5"),
            new StepDefinition
            {
                Id = "step5",
                Name = "Step 5",
                Type = "agent",
                Agent = "FailAgent",
                Input = new(),
                Output = new(),
                OnFailure = new OnFailureDefinition { Action = "stop", Message = "Critical failure" },
                Next = "step6"
            },
            InputStep("step6", next: "step7"),
            OutputStep("step7"));

        var stateStore = new InMemoryStateStore();
        var engine = CreateEngine(workflow, new AlwaysFailAgent(), stateStore);
        var context = new ExecutionContext { Inputs = new() { ["data"] = "test" } };

        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("failed", result.Status);

        // Checkpoint should have steps 1-4 completed (step 5 failed)
        var checkpointFile = Path.Combine(_checkpointDir, "TestWorkflow-checkpoint.json");
        Assert.True(File.Exists(checkpointFile));

        var json = File.ReadAllText(checkpointFile);
        var checkpoint = JsonSerializer.Deserialize<CheckpointData>(json)!;
        Assert.Equal(4, checkpoint.CompletedSteps.Count);
        Assert.Contains("step1", checkpoint.CompletedSteps);
        Assert.Contains("step4", checkpoint.CompletedSteps);
        Assert.DoesNotContain("step5", checkpoint.CompletedSteps);
        Assert.DoesNotContain("step6", checkpoint.CompletedSteps);
    }

    // ──────── Test 3: Resume from checkpoint — skips completed steps ────────

    [Fact]
    public async Task Resume_FromCheckpoint_SkipsCompletedStepsAndFinishes()
    {
        // 7-step workflow: step 1 input, steps 2-6 agent, step 7 output
        var agents = new[] { "Agent2", "Agent3", "Agent4", "Agent5", "Agent6" };
        var workflow = CreateWorkflow(
            InputStep("step1", next: "step2"),
            AgentStep("step2", "Agent2", next: "step3"),
            AgentStep("step3", "Agent3", next: "step4"),
            AgentStep("step4", "Agent4", next: "step5"),
            AgentStep("step5", "Agent5", next: "step6"),
            AgentStep("step6", "Agent6", next: "step7"),
            OutputStep("step7"));

        // Phase 1: Run with agent that fails on step 5
        var allAgents = MakeAgents("Agent2", "Agent3", "Agent4", "Agent5", "Agent6");
        var failOnStep5 = new FailOnAgentExecutor("Agent5");
        var stateStore1 = new InMemoryStateStore();
        var engine1 = CreateEngine(workflow, failOnStep5, stateStore1, agents: allAgents);
        var context = new ExecutionContext { Inputs = new() { ["data"] = "test-value" } };

        var result1 = await engine1.ExecuteAsync(context, CancellationToken.None);
        Assert.Equal("failed", result1.Status);

        var checkpointFile = Path.Combine(_checkpointDir, "TestWorkflow-checkpoint.json");
        Assert.True(File.Exists(checkpointFile));

        // Phase 2: Resume with a working agent
        var trackingAgent = new TrackingAgent();
        var stateStore2 = new InMemoryStateStore();
        var engine2 = CreateEngine(workflow, trackingAgent, stateStore2, agents: allAgents);

        var result2 = await engine2.ResumeAsync(checkpointFile, CancellationToken.None);

        Assert.Equal("completed", result2.Status);
        Assert.Empty(result2.Errors);

        // Should have only executed steps 5 and 6 (agents), step 7 (output)
        // Steps 1-4 should have been skipped
        Assert.DoesNotContain("step1", trackingAgent.ExecutedStepIds);
        Assert.DoesNotContain("step2", trackingAgent.ExecutedStepIds);
        Assert.DoesNotContain("step3", trackingAgent.ExecutedStepIds);
        Assert.DoesNotContain("step4", trackingAgent.ExecutedStepIds);
        Assert.Contains("step5", trackingAgent.ExecutedStepIds);
        Assert.Contains("step6", trackingAgent.ExecutedStepIds);
    }

    // ──────── Test 4: Resumed state store has data from checkpoint ────────

    [Fact]
    public async Task Resume_RestoresStateFromCheckpoint()
    {
        var workflow = CreateWorkflow(
            InputStep("step1", next: "step2"),
            InputStep("step2"));

        var stateStore = new InMemoryStateStore();
        var engine = CreateEngine(workflow, new SuccessAgent(), stateStore);
        var context = new ExecutionContext { Inputs = new() { ["data"] = "persisted-value" } };

        await engine.ExecuteAsync(context, CancellationToken.None);

        var checkpointFile = Path.Combine(_checkpointDir, "TestWorkflow-checkpoint.json");
        Assert.True(File.Exists(checkpointFile));

        // Resume into a fresh state store
        var freshStore = new InMemoryStateStore();
        var engine2 = CreateEngine(workflow, new SuccessAgent(), freshStore);

        await engine2.ResumeAsync(checkpointFile, CancellationToken.None);

        // Fresh store should have the state from the checkpoint
        Assert.True(freshStore.Has("steps.step1.output.data"));
        Assert.Equal("persisted-value", freshStore.Get<string>("steps.step1.output.data"));
    }

    // ──────── Test 5: No checkpoint without directory ────────

    [Fact]
    public async Task Execute_WithoutCheckpointDir_DoesNotCreateCheckpoint()
    {
        var workflow = CreateWorkflow(InputStep("step1"));

        var stateStore = new InMemoryStateStore();
        var engine = new WorkflowEngine(
            workflow, [], [], [], [],
            new SuccessAgent(),
            stateStore,
            _logger,
            checkpointDirectory: null);

        var context = new ExecutionContext { Inputs = new() { ["data"] = "test" } };
        var result = await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Empty(Directory.GetFiles(_checkpointDir));
    }

    // ──────── Test 6: Interrupt at step 5, resume, complete all steps ────────

    [Fact]
    public async Task InterruptAtStep5_ResumeFromCheckpoint_CompletesAllRemainingSteps()
    {
        // 8-step workflow matching the task description scenario
        var workflow = CreateWorkflow(
            InputStep("step1", next: "step2"),
            AgentStep("step2", "A2", next: "step3"),
            AgentStep("step3", "A3", next: "step4"),
            AgentStep("step4", "A4", next: "step5"),
            new StepDefinition
            {
                Id = "step5",
                Name = "Step 5 - Fails",
                Type = "agent",
                Agent = "A5",
                Input = new(),
                Output = new() { ["result"] = "result" },
                OnFailure = new OnFailureDefinition { Action = "stop", Message = "Interrupted" },
                Next = "step6"
            },
            AgentStep("step6", "A6", next: "step7"),
            AgentStep("step7", "A7", next: "step8"),
            OutputStep("step8"));

        // Phase 1: Execute — fails at step 5
        var allAgents = MakeAgents("A2", "A3", "A4", "A5", "A6", "A7");
        var failAgent = new FailOnAgentExecutor("A5");
        var store1 = new InMemoryStateStore();
        var engine1 = CreateEngine(workflow, failAgent, store1, agents: allAgents);

        var result1 = await engine1.ExecuteAsync(
            new ExecutionContext { Inputs = new() { ["data"] = "input" } },
            CancellationToken.None);

        Assert.Equal("failed", result1.Status);

        var checkpointFile = Path.Combine(_checkpointDir, "TestWorkflow-checkpoint.json");
        Assert.True(File.Exists(checkpointFile));

        // Verify checkpoint has 4 completed steps (step1 through step4)
        var checkpoint = JsonSerializer.Deserialize<CheckpointData>(File.ReadAllText(checkpointFile))!;
        Assert.Equal(4, checkpoint.CompletedSteps.Count);
        Assert.Equal("step4", checkpoint.LastCompletedStep);

        // Phase 2: Resume with a working agent
        var tracker = new TrackingAgent();
        var store2 = new InMemoryStateStore();
        var engine2 = CreateEngine(workflow, tracker, store2, agents: allAgents);

        var result2 = await engine2.ResumeAsync(checkpointFile, CancellationToken.None);

        Assert.Equal("completed", result2.Status);
        Assert.Empty(result2.Errors);

        // Only steps 5, 6, 7 should have used agents (step 8 is output)
        Assert.Equal(3, tracker.ExecutedStepIds.Count);
        Assert.Contains("step5", tracker.ExecutedStepIds);
        Assert.Contains("step6", tracker.ExecutedStepIds);
        Assert.Contains("step7", tracker.ExecutedStepIds);
    }

    // ──────────────── Test doubles ────────────────

    private class SuccessAgent : IAgentExecutor
    {
        public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct)
            => Task.FromResult(new AgentResult { Status = "success", Message = "OK" });
    }

    private class AlwaysFailAgent : IAgentExecutor
    {
        public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct)
            => Task.FromResult(new AgentResult { Status = "error", Message = "Fail" });
    }

    private class FailOnAgentExecutor : IAgentExecutor
    {
        private readonly string _failAgentName;

        public FailOnAgentExecutor(string failAgentName) => _failAgentName = failAgentName;

        public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct)
        {
            if (string.Equals(request.Agent.Name, _failAgentName, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new AgentResult { Status = "error", Message = $"Fail on {_failAgentName}" });

            return Task.FromResult(new AgentResult
            {
                Status = "success",
                Data = new() { ["result"] = $"ok-{request.Agent.Name}" },
                Message = "OK"
            });
        }
    }

    private class TrackingAgent : IAgentExecutor
    {
        public List<string> ExecutedStepIds { get; } = [];

        public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct)
        {
            ExecutedStepIds.Add(request.Context?.CurrentStep ?? request.Agent.Name);
            return Task.FromResult(new AgentResult
            {
                Status = "success",
                Data = new() { ["result"] = "ok" },
                Message = "OK"
            });
        }
    }
}
