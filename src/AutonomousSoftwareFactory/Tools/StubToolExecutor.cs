namespace AutonomousSoftwareFactory.Tools;

using AutonomousSoftwareFactory.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Placeholder implementation for Phase 2.2. Will be replaced by ToolExecutor in Phase 2.3.
/// </summary>
public class StubToolExecutor : IToolExecutor
{
    private readonly ILogger<StubToolExecutor> _logger;

    public StubToolExecutor(ILogger<StubToolExecutor> logger)
    {
        _logger = logger;
    }

    public Task<ToolResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        _logger.LogWarning("StubToolExecutor invoked for tool '{Tool}'. " +
            "Replace with real ToolExecutor in Phase 2.3.", request.Tool?.Name);

        return Task.FromResult(new ToolResult
        {
            Success = true,
            Output = new Dictionary<string, object> { ["result"] = "Stub tool execution — no real action performed" }
        });
    }
}
