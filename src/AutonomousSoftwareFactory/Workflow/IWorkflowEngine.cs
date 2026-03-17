namespace AutonomousSoftwareFactory.Workflow;

using AutonomousSoftwareFactory.Models;

public interface IWorkflowEngine
{
    Task<ExecutionResult> ExecuteAsync(ExecutionContext context, CancellationToken ct);
    Task<ExecutionResult> ResumeAsync(string checkpointPath, CancellationToken ct);
}
