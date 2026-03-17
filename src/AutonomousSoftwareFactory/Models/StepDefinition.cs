namespace AutonomousSoftwareFactory.Models;

/// <summary>
/// Represents a single step in the workflow.
/// Properties: id, name, type, description, agent, input, output, validations, next, retry, on_failure.
/// </summary>
public class StepDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Agent { get; set; }
    public Dictionary<string, object> Input { get; set; } = new();
    public Dictionary<string, object> Output { get; set; } = new();
    public List<StepValidation>? Validations { get; set; }
    public string? Next { get; set; }
    public RetryPolicy? Retry { get; set; }
    public OnFailureDefinition? OnFailure { get; set; }
}

public class StepValidation
{
    public string Field { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
}

public class RetryPolicy
{
    public int MaxAttempts { get; set; } = 1;
    public string Strategy { get; set; } = "simple";
}

public class OnFailureDefinition
{
    public string Action { get; set; } = "stop";
    public string Message { get; set; } = string.Empty;
}
