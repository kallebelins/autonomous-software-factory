namespace AutonomousSoftwareFactory.Agents;

using AutonomousSoftwareFactory.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Placeholder implementation for Phase 1. Will be replaced by AgentExecutor in Phase 2.
/// </summary>
public class StubAgentExecutor : IAgentExecutor
{
    private readonly ILogger<StubAgentExecutor> _logger;

    public StubAgentExecutor(ILogger<StubAgentExecutor> logger)
    {
        _logger = logger;
    }

    public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct)
    {
        _logger.LogWarning("StubAgentExecutor invoked for agent '{Agent}'. " +
            "Replace with real AgentExecutor in Phase 2.", request.Agent?.Name);

        return Task.FromResult(new AgentResult
        {
            Status = "success",
            Message = "Stub execution — no LLM call performed"
        });
    }
}
