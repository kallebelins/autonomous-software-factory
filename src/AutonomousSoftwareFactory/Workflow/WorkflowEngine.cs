namespace AutonomousSoftwareFactory.Workflow;

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutonomousSoftwareFactory.Agents;
using AutonomousSoftwareFactory.Logging;
using AutonomousSoftwareFactory.Models;
using AutonomousSoftwareFactory.State;
using Microsoft.Extensions.Logging;

public partial class WorkflowEngine : IWorkflowEngine
{
    private readonly WorkflowDefinition _workflow;
    private readonly List<AgentDefinition> _agents;
    private readonly List<SkillDefinition> _skills;
    private readonly List<ToolDefinition> _tools;
    private readonly List<PromptDefinition> _prompts;
    private readonly IAgentExecutor _agentExecutor;
    private readonly IStateStore _stateStore;
    private readonly ILogger<WorkflowEngine> _logger;
    private readonly IRunLogger _runLogger;
    private readonly string? _checkpointDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public WorkflowEngine(
        WorkflowDefinition workflow,
        List<AgentDefinition> agents,
        List<SkillDefinition> skills,
        List<ToolDefinition> tools,
        List<PromptDefinition> prompts,
        IAgentExecutor agentExecutor,
        IStateStore stateStore,
        ILogger<WorkflowEngine> logger,
        IRunLogger? runLogger = null,
        string? checkpointDirectory = null)
    {
        _workflow = workflow;
        _agents = agents;
        _skills = skills;
        _tools = tools;
        _prompts = prompts;
        _agentExecutor = agentExecutor;
        _stateStore = stateStore;
        _logger = logger;
        _runLogger = runLogger ?? NullRunLogger.Instance;
        _checkpointDirectory = checkpointDirectory;
    }

    public Task<ExecutionResult> ExecuteAsync(ExecutionContext context, CancellationToken ct)
        => ExecuteInternalAsync(context, new HashSet<string>(), ct);

    public async Task<ExecutionResult> ResumeAsync(string checkpointPath, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(checkpointPath, ct);
        var checkpoint = JsonSerializer.Deserialize<CheckpointData>(json, JsonOptions)
            ?? throw new InvalidOperationException("Invalid checkpoint file");

        // Restore state store from checkpoint
        foreach (var (key, value) in checkpoint.StateData)
            _stateStore.Set(key, value);

        var context = new ExecutionContext
        {
            Inputs = checkpoint.Inputs
        };

        _logger.LogInformation("Resuming workflow '{Name}' from checkpoint. Completed steps: {Steps}",
            _workflow.Name, string.Join(", ", checkpoint.CompletedSteps));

        return await ExecuteInternalAsync(context, checkpoint.CompletedSteps.ToHashSet(), ct);
    }

    private async Task<ExecutionResult> ExecuteInternalAsync(
        ExecutionContext context, HashSet<string> completedSteps, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ExecutionResult { Status = "running" };

        var stepsById = _workflow.Steps.ToDictionary(s => s.Id);
        var currentStepId = _workflow.Steps.FirstOrDefault()?.Id;

        _logger.LogInformation("Workflow '{Name}' started with {Count} steps",
            _workflow.Name, _workflow.Steps.Count);
        _runLogger.LogWorkflowStart(_workflow.Name, _workflow.Steps.Count);

        while (currentStepId is not null)
        {
            ct.ThrowIfCancellationRequested();

            if (!stepsById.TryGetValue(currentStepId, out var step))
            {
                result.Errors.Add($"Step '{currentStepId}' not found in workflow");
                result.Status = "failed";
                break;
            }

            // Skip steps already completed in a previous run (checkpoint resume)
            if (completedSteps.Contains(step.Id))
            {
                _logger.LogInformation("Skipping already completed step '{StepId}'", step.Id);
                currentStepId = step.Next;
                continue;
            }

            context.CurrentStep = step.Id;
            var stepSuccess = await ExecuteStepWithRetryAsync(step, context, result, ct);

            if (!stepSuccess)
            {
                var action = step.OnFailure?.Action ?? "stop";
                if (action == "stop")
                {
                    _logger.LogError("Workflow stopped at step '{StepId}': {Message}",
                        step.Id, step.OnFailure?.Message ?? "Step failed");
                    result.Status = "failed";

                    // Save checkpoint on failure so execution can be resumed
                    await SaveCheckpointAsync(context, completedSteps);
                    break;
                }

                _logger.LogWarning("Step '{StepId}' failed but workflow continues: {Message}",
                    step.Id, step.OnFailure?.Message ?? "Continuing after failure");
            }

            if (stepSuccess)
            {
                completedSteps.Add(step.Id);
                await SaveCheckpointAsync(context, completedSteps);
            }

            currentStepId = step.Next;
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        if (result.Status == "running")
            result.Status = "completed";

        _logger.LogInformation("Workflow '{Name}' finished with status '{Status}' in {Duration}",
            _workflow.Name, result.Status, result.Duration);
        _runLogger.LogWorkflowEnd(_workflow.Name, result.Status, result.Duration);

        return result;
    }

    private async Task SaveCheckpointAsync(ExecutionContext context, HashSet<string> completedSteps)
    {
        if (_checkpointDirectory is null) return;

        Directory.CreateDirectory(_checkpointDirectory);

        var checkpoint = new CheckpointData
        {
            WorkflowName = _workflow.Name,
            CompletedSteps = completedSteps.ToList(),
            LastCompletedStep = completedSteps.LastOrDefault(),
            Inputs = context.Inputs,
            StateData = _stateStore.GetAll(),
            Timestamp = DateTime.UtcNow
        };

        var filePath = Path.Combine(_checkpointDirectory, $"{_workflow.Name}-checkpoint.json");
        var json = JsonSerializer.Serialize(checkpoint, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        _logger.LogInformation("Checkpoint saved after step '{StepId}' to {Path}",
            checkpoint.LastCompletedStep, filePath);
    }

    private async Task<bool> ExecuteStepWithRetryAsync(
        StepDefinition step, ExecutionContext context, ExecutionResult result, CancellationToken ct)
    {
        var maxAttempts = step.Retry?.MaxAttempts ?? _workflow.Policies.Retry.MaxAttempts;
        if (maxAttempts < 1) maxAttempts = 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            _logger.LogInformation("Step '{StepId}' ({Type}) — attempt {Attempt}/{Max}",
                step.Id, step.Type, attempt, maxAttempts);
            _runLogger.LogStepStart(step.Id, step.Name, step.Type, attempt, maxAttempts);

            var stepStopwatch = Stopwatch.StartNew();
            try
            {
                var stepResult = await ExecuteStepAsync(step, context, ct);
                stepStopwatch.Stop();

                if (stepResult)
                {
                    _logger.LogInformation("Step '{StepId}' completed successfully", step.Id);
                    _runLogger.LogStepEnd(step.Id, "success", stepStopwatch.Elapsed);
                    return true;
                }

                _runLogger.LogStepEnd(step.Id, "failed", stepStopwatch.Elapsed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                stepStopwatch.Stop();
                _logger.LogError(ex, "Step '{StepId}' threw exception on attempt {Attempt}", step.Id, attempt);
                result.Errors.Add($"Step '{step.Id}' attempt {attempt}: {ex.Message}");
                _runLogger.LogStepEnd(step.Id, "error", stepStopwatch.Elapsed, [ex.Message]);
            }

            if (attempt < maxAttempts)
                _logger.LogInformation("Retrying step '{StepId}'...", step.Id);
        }

        result.Errors.Add($"Step '{step.Id}' failed after {maxAttempts} attempt(s)");
        return false;
    }

    private async Task<bool> ExecuteStepAsync(StepDefinition step, ExecutionContext context, CancellationToken ct)
    {
        var resolvedInputs = ResolveInputs(step.Input, context);

        return step.Type switch
        {
            "input" => ExecuteInputStep(step, resolvedInputs, context),
            "agent" => await ExecuteAgentStepAsync(step, resolvedInputs, context, ct),
            "output" => ExecuteOutputStep(step, resolvedInputs),
            _ => throw new InvalidOperationException($"Unknown step type '{step.Type}'")
        };
    }

    private bool ExecuteInputStep(StepDefinition step, Dictionary<string, object> resolvedInputs, ExecutionContext context)
    {
        if (step.Validations is not null)
        {
            foreach (var validation in step.Validations)
            {
                if (validation.Rule == "required"
                    && (!resolvedInputs.TryGetValue(validation.Field, out var value) || value is null or ""))
                {
                    _logger.LogError("Validation failed: field '{Field}' is required in step '{StepId}'",
                        validation.Field, step.Id);
                    return false;
                }
            }
        }

        SaveStepOutputs(step, resolvedInputs);
        return true;
    }

    private async Task<bool> ExecuteAgentStepAsync(
        StepDefinition step, Dictionary<string, object> resolvedInputs, ExecutionContext context, CancellationToken ct)
    {
        var agent = _agents.FirstOrDefault(a =>
            string.Equals(a.Name, step.Agent, StringComparison.OrdinalIgnoreCase));

        if (agent is null)
        {
            _logger.LogError("Agent '{Agent}' not found for step '{StepId}'", step.Agent, step.Id);
            return false;
        }

        var agentSkills = _skills
            .Where(s => agent.Skills.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var agentTools = _tools
            .Where(t => agent.Tools.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var prompt = _prompts
            .FirstOrDefault(p => string.Equals(p.Key, agent.Prompt, StringComparison.OrdinalIgnoreCase));

        var request = new AgentExecutionRequest
        {
            Agent = agent,
            Inputs = resolvedInputs,
            Skills = agentSkills,
            Tools = agentTools,
            Prompt = prompt?.Template ?? agent.Prompt,
            Context = context
        };

        var agentResult = await _agentExecutor.ExecuteAsync(request, ct);

        if (agentResult.Status != "success")
        {
            _logger.LogError("Agent '{Agent}' returned status '{Status}': {Message}",
                agent.Name, agentResult.Status, agentResult.Message);
            return false;
        }

        // Merge agent output data into step outputs
        var outputData = new Dictionary<string, object>(resolvedInputs);
        foreach (var (key, value) in agentResult.Data)
            outputData[key] = value;

        SaveStepOutputs(step, outputData);
        return true;
    }

    private bool ExecuteOutputStep(StepDefinition step, Dictionary<string, object> resolvedInputs)
    {
        SaveStepOutputs(step, resolvedInputs);

        // For output steps, also store static output values defined in the YAML
        foreach (var (key, value) in step.Output)
        {
            if (value is Dictionary<object, object> dict)
            {
                var resolved = new Dictionary<string, object>();
                foreach (var (dk, dv) in dict)
                    resolved[dk.ToString()!] = ResolveTemplate(dv, null!);
                _stateStore.Set($"steps.{step.Id}.output.{key}", resolved);
            }
            else
            {
                var resolvedValue = ResolveTemplate(value, null!);
                _stateStore.Set($"steps.{step.Id}.output.{key}", resolvedValue);
            }
        }

        return true;
    }

    private void SaveStepOutputs(StepDefinition step, Dictionary<string, object> data)
    {
        foreach (var (outputKey, outputMapping) in step.Output)
        {
            var mappingStr = outputMapping?.ToString() ?? "";

            // The output mapping value is the key in the resolved data
            if (data.TryGetValue(mappingStr, out var value))
                _stateStore.Set($"steps.{step.Id}.output.{outputKey}", value);
            else if (data.TryGetValue(outputKey, out var directValue))
                _stateStore.Set($"steps.{step.Id}.output.{outputKey}", directValue);
        }
    }

    private Dictionary<string, object> ResolveInputs(Dictionary<string, object> inputs, ExecutionContext context)
    {
        var resolved = new Dictionary<string, object>();
        foreach (var (key, value) in inputs)
            resolved[key] = ResolveTemplate(value, context);
        return resolved;
    }

    private object ResolveTemplate(object value, ExecutionContext context)
    {
        if (value is not string template)
            return value;

        var match = TemplatePattern().Match(template);

        if (!match.Success)
            return template;

        // If the entire string is a single template expression, return the resolved object directly
        if (match.Value == template)
            return ResolveExpression(match.Groups[1].Value, context) ?? template;

        // Otherwise, do string interpolation for embedded templates
        return TemplatePattern().Replace(template, m =>
            ResolveExpression(m.Groups[1].Value, context)?.ToString() ?? "");
    }

    private object? ResolveExpression(string expression, ExecutionContext context)
    {
        // {{context.inputs.X}}
        if (expression.StartsWith("context.inputs."))
        {
            var key = expression["context.inputs.".Length..];
            return context?.Inputs.GetValueOrDefault(key);
        }

        // {{steps.X.output.Y}}
        if (expression.StartsWith("steps."))
        {
            var stateKey = expression; // stored as "steps.X.output.Y"
            if (_stateStore.Has(stateKey))
                return _stateStore.Get<object>(stateKey);
        }

        return null;
    }

    [GeneratedRegex(@"\{\{(.+?)\}\}")]
    private static partial Regex TemplatePattern();
}
