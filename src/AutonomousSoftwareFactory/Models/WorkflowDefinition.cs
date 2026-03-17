namespace AutonomousSoftwareFactory.Models;

/// <summary>
/// Root document for deserializing workflow.yaml.
/// </summary>
public class WorkflowDocument
{
    public string Version { get; set; } = string.Empty;
    public WorkflowDefinition Workflow { get; set; } = new();
}

/// <summary>
/// Maps the "workflow" section of workflow.yaml.
/// Properties: name, description, execution, context, policies, steps.
/// </summary>
public class WorkflowDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ExecutionSettings Execution { get; set; } = new();
    public ContextDefinition Context { get; set; } = new();
    public PoliciesDefinition Policies { get; set; } = new();
    public List<StepDefinition> Steps { get; set; } = new();
}

public class ExecutionSettings
{
    public string Mode { get; set; } = "sequential";
    public bool ContinueOnError { get; set; }
    public bool PersistState { get; set; }
    public bool EnableCheckpoints { get; set; }
}

public class ContextDefinition
{
    public List<string> Inputs { get; set; } = new();
    public List<string> SharedState { get; set; } = new();
}

public class PoliciesDefinition
{
    public RetryPolicy Retry { get; set; } = new();
    public ValidationPolicy Validation { get; set; } = new();
    public SecurityPolicy Security { get; set; } = new();
}

public class ValidationPolicy
{
    public bool RequireNonEmptyOutputs { get; set; }
    public bool StopOnCriticalFailure { get; set; }
}

public class SecurityPolicy
{
    public bool AllowGitWrite { get; set; }
    public bool AllowDependencyInstallation { get; set; }
    public bool AllowBuildExecution { get; set; }
    public bool AllowTestExecution { get; set; }
    public bool AllowPullRequestCreation { get; set; }
}
