namespace AutonomousSoftwareFactory.Tools;

using AutonomousSoftwareFactory.Models;

public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken ct);
}
