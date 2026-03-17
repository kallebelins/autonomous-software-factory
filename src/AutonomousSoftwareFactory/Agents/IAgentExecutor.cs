namespace AutonomousSoftwareFactory.Agents;

using AutonomousSoftwareFactory.Models;

public interface IAgentExecutor
{
    Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct);
}
