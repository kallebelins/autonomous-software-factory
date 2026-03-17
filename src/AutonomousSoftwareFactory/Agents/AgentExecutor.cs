namespace AutonomousSoftwareFactory.Agents;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutonomousSoftwareFactory.Llm;
using AutonomousSoftwareFactory.Logging;
using AutonomousSoftwareFactory.Models;
using AutonomousSoftwareFactory.Tools;
using Microsoft.Extensions.Logging;

public class AgentExecutor : IAgentExecutor
{
    private readonly ILlmClient _llmClient;
    private readonly IToolExecutor _toolExecutor;
    private readonly List<PromptDefinition> _prompts;
    private readonly ILogger<AgentExecutor> _logger;
    private readonly IRunLogger _runLogger;

    private const int MaxToolRounds = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AgentExecutor(
        ILlmClient llmClient,
        IToolExecutor toolExecutor,
        List<PromptDefinition> prompts,
        ILogger<AgentExecutor> logger,
        IRunLogger? runLogger = null)
    {
        _llmClient = llmClient;
        _toolExecutor = toolExecutor;
        _prompts = prompts;
        _logger = logger;
        _runLogger = runLogger ?? NullRunLogger.Instance;
    }

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct)
    {
        _logger.LogInformation("AgentExecutor starting for agent '{Agent}'", request.Agent.Name);

        var prompt = BuildPrompt(request);

        _logger.LogDebug("Built prompt ({Length} chars) for agent '{Agent}'", prompt.Length, request.Agent.Name);

        for (var round = 0; round < MaxToolRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            var llmResponse = await _llmClient.CompleteAsync(prompt, ct);

            var parsed = ParseLlmResponse(llmResponse);

            if (parsed.ToolCalls is not { Count: > 0 })
            {
                _logger.LogInformation("Agent '{Agent}' completed with status '{Status}'",
                    request.Agent.Name, parsed.Status);
                return new AgentResult
                {
                    Status = parsed.Status ?? "success",
                    Data = parsed.Data ?? new Dictionary<string, object>(),
                    Message = parsed.Message ?? string.Empty
                };
            }

            if (request.Tools.Count == 0)
            {
                _logger.LogWarning("Agent '{Agent}' requested tool calls but has no tools assigned. " +
                    "Returning response without tool execution.", request.Agent.Name);
                return new AgentResult
                {
                    Status = parsed.Status ?? "success",
                    Data = parsed.Data ?? new Dictionary<string, object>(),
                    Message = parsed.Message ?? string.Empty
                };
            }

            _logger.LogInformation("Agent '{Agent}' requested {Count} tool call(s) — round {Round}",
                request.Agent.Name, parsed.ToolCalls.Count, round + 1);

            var toolResults = await ExecuteToolCallsAsync(parsed.ToolCalls, request.Tools, ct);

            prompt = AppendToolResults(prompt, parsed.ToolCalls, toolResults);
        }

        _logger.LogWarning("Agent '{Agent}' exceeded max tool rounds ({Max})", request.Agent.Name, MaxToolRounds);
        return new AgentResult
        {
            Status = "error",
            Message = $"Agent exceeded maximum tool interaction rounds ({MaxToolRounds})"
        };
    }

    internal string BuildPrompt(AgentExecutionRequest request)
    {
        var sb = new StringBuilder();

        // 1. System prompt
        var systemPrompt = GetPromptTemplate("system");
        if (systemPrompt is not null)
            sb.AppendLine(systemPrompt).AppendLine();

        // 2. Context injection
        var contextInjection = GetPromptTemplate("context_injection");
        if (contextInjection is not null)
        {
            var contextBlock = ReplaceContextPlaceholders(contextInjection, request.Context);
            sb.AppendLine(contextBlock).AppendLine();
        }

        // 3. Skills instructions
        if (request.Skills.Count > 0)
        {
            sb.AppendLine("## Skills");
            foreach (var skill in request.Skills)
            {
                sb.AppendLine($"### {skill.Name} ({skill.Type})");
                sb.AppendLine(skill.Instructions);
                if (skill.Constraints.Count > 0)
                {
                    sb.AppendLine("Constraints:");
                    foreach (var c in skill.Constraints)
                        sb.AppendLine($"- {c}");
                }
                sb.AppendLine();
            }
        }

        // 4. Available tools
        if (request.Tools.Count > 0)
        {
            sb.AppendLine("## Available Tools");
            sb.AppendLine("You can request tool execution by including a \"tool_calls\" array in your JSON response.");
            sb.AppendLine("Each tool call must have: { \"tool\": \"tool_name\", \"inputs\": { ... } }");
            sb.AppendLine();
            foreach (var tool in request.Tools)
            {
                sb.AppendLine($"### {tool.Name}");
                sb.AppendLine($"- Description: {tool.Description}");
                if (tool.Input.Count > 0)
                {
                    sb.AppendLine("- Input:");
                    foreach (var (key, desc) in tool.Input)
                        sb.AppendLine($"  - {key}: {desc}");
                }
                sb.AppendLine();
            }
        }

        // 5. Output format
        var outputFormat = GetPromptTemplate("output_format");
        if (outputFormat is not null)
            sb.AppendLine(outputFormat).AppendLine();

        // 6. Agent-specific prompt
        if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            sb.AppendLine("## Agent Instructions");
            sb.AppendLine(request.Prompt).AppendLine();
        }

        // 7. Step inputs
        if (request.Inputs.Count > 0)
        {
            sb.AppendLine("## Inputs");
            sb.AppendLine(JsonSerializer.Serialize(request.Inputs, JsonOptions));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string? GetPromptTemplate(string key)
    {
        return _prompts
            .FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Template;
    }

    private static string ReplaceContextPlaceholders(string template, ExecutionContext context)
    {
        var result = template;
        foreach (var (key, value) in context.Inputs)
        {
            var placeholder = $"{{{{{key}}}}}";
            result = result.Replace(placeholder, value?.ToString() ?? "");
        }
        foreach (var (key, value) in context.SharedState)
        {
            var placeholder = $"{{{{{key}}}}}";
            result = result.Replace(placeholder, value?.ToString() ?? "");
        }
        return result;
    }

    internal LlmParsedResponse ParseLlmResponse(string response)
    {
        var trimmed = response.Trim();

        // Try to extract JSON from markdown code blocks
        if (trimmed.StartsWith("```"))
        {
            var startIdx = trimmed.IndexOf('\n');
            var endIdx = trimmed.LastIndexOf("```");
            if (startIdx >= 0 && endIdx > startIdx)
                trimmed = trimmed[(startIdx + 1)..endIdx].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var parsed = new LlmParsedResponse
            {
                Status = root.TryGetProperty("status", out var s) ? s.GetString() : "success",
                Message = root.TryGetProperty("message", out var m) ? m.GetString() : null
            };

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                parsed.Data = JsonSerializer.Deserialize<Dictionary<string, object>>(data.GetRawText(), JsonOptions)
                    ?? new Dictionary<string, object>();
            }

            if (root.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            {
                parsed.ToolCalls = new List<ToolCallRequest>();
                foreach (var tc in toolCalls.EnumerateArray())
                {
                    var toolName = tc.TryGetProperty("tool", out var tn) ? tn.GetString() ?? "" : "";
                    var inputs = new Dictionary<string, string>();
                    if (tc.TryGetProperty("inputs", out var inp) && inp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in inp.EnumerateObject())
                            inputs[prop.Name] = prop.Value.ToString();
                    }
                    parsed.ToolCalls.Add(new ToolCallRequest { Tool = toolName, Inputs = inputs });
                }
            }

            return parsed;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response as JSON, treating as plain text");
            return new LlmParsedResponse
            {
                Status = "success",
                Data = new Dictionary<string, object> { ["raw_response"] = trimmed },
                Message = trimmed
            };
        }
    }

    private async Task<List<ToolResult>> ExecuteToolCallsAsync(
        List<ToolCallRequest> toolCalls, List<ToolDefinition> availableTools, CancellationToken ct)
    {
        var results = new List<ToolResult>();

        foreach (var call in toolCalls)
        {
            var toolDef = availableTools.FirstOrDefault(t =>
                string.Equals(t.Name, call.Tool, StringComparison.OrdinalIgnoreCase));

            if (toolDef is null)
            {
                _logger.LogWarning("Tool '{Tool}' not found in agent's available tools", call.Tool);
                results.Add(new ToolResult
                {
                    Success = false,
                    Errors = [$"Tool '{call.Tool}' not found"]
                });
                continue;
            }

            var request = new ToolExecutionRequest
            {
                Tool = toolDef,
                Inputs = call.Inputs
            };

            _logger.LogInformation("Executing tool '{Tool}'", call.Tool);
            var result = await _toolExecutor.ExecuteAsync(request, ct);
            results.Add(result);

            _logger.LogInformation("Tool '{Tool}' completed — success={Success}", call.Tool, result.Success);

            var outputSummary = result.Output.Count > 0
                ? JsonSerializer.Serialize(result.Output, JsonOptions)
                : null;
            _runLogger.LogToolExecution(
                call.Tool,
                toolDef.ExecutionType,
                command: null,
                result.Success,
                outputSummary,
                result.Errors.Count > 0 ? result.Errors : null);
        }

        return results;
    }

    private static string AppendToolResults(string prompt, List<ToolCallRequest> calls, List<ToolResult> results)
    {
        var sb = new StringBuilder(prompt);
        sb.AppendLine("## Tool Execution Results");

        for (var i = 0; i < calls.Count; i++)
        {
            sb.AppendLine($"### {calls[i].Tool}");
            if (i < results.Count)
            {
                sb.AppendLine($"- Success: {results[i].Success}");
                if (results[i].Output.Count > 0)
                    sb.AppendLine($"- Output: {JsonSerializer.Serialize(results[i].Output, JsonOptions)}");
                if (results[i].Errors.Count > 0)
                    sb.AppendLine($"- Errors: {string.Join("; ", results[i].Errors)}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Based on the tool results above, provide your final response in the required JSON format.");
        sb.AppendLine();

        return sb.ToString();
    }

    internal class LlmParsedResponse
    {
        public string? Status { get; set; }
        public Dictionary<string, object>? Data { get; set; }
        public string? Message { get; set; }
        public List<ToolCallRequest>? ToolCalls { get; set; }
    }

    internal class ToolCallRequest
    {
        public string Tool { get; set; } = string.Empty;
        public Dictionary<string, string> Inputs { get; set; } = new();
    }
}
