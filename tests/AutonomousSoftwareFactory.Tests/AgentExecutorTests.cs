namespace AutonomousSoftwareFactory.Tests;

using AutonomousSoftwareFactory.Agents;
using AutonomousSoftwareFactory.Llm;
using AutonomousSoftwareFactory.Models;
using AutonomousSoftwareFactory.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class AgentExecutorTests
{
    private readonly List<PromptDefinition> _defaultPrompts =
    [
        new() { Key = "system", Template = "You are a specialized agent." },
        new() { Key = "context_injection", Template = "## Context\nStack: {{stack}}" },
        new() { Key = "output_format", Template = "Respond in valid JSON with status, data, message." }
    ];

    private AgentExecutor CreateExecutor(
        ILlmClient? llmClient = null,
        IToolExecutor? toolExecutor = null,
        List<PromptDefinition>? prompts = null)
    {
        return new AgentExecutor(
            llmClient ?? new FakeLlmClient("{}"),
            toolExecutor ?? new FakeToolExecutor(),
            prompts ?? _defaultPrompts,
            new NullLogger<AgentExecutor>());
    }

    private static AgentExecutionRequest CreateRequest(
        AgentDefinition? agent = null,
        Dictionary<string, object>? inputs = null,
        List<SkillDefinition>? skills = null,
        List<ToolDefinition>? tools = null,
        string? prompt = null) => new()
    {
        Agent = agent ?? new AgentDefinition { Name = "TestAgent", Prompt = "Do something." },
        Inputs = inputs ?? new(),
        Skills = skills ?? [],
        Tools = tools ?? [],
        Prompt = prompt ?? "Analyze the input.",
        Context = new ExecutionContext
        {
            Inputs = new() { ["stack"] = ".NET 8" }
        }
    };

    // --- Prompt building tests ---

    [Fact]
    public void BuildPrompt_IncludesSystemPrompt()
    {
        var executor = CreateExecutor();
        var request = CreateRequest();

        var prompt = executor.BuildPrompt(request);

        Assert.Contains("You are a specialized agent.", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesContextInjection_WithPlaceholdersReplaced()
    {
        var executor = CreateExecutor();
        var request = CreateRequest();

        var prompt = executor.BuildPrompt(request);

        Assert.Contains("Stack: .NET 8", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesOutputFormat()
    {
        var executor = CreateExecutor();
        var request = CreateRequest();

        var prompt = executor.BuildPrompt(request);

        Assert.Contains("Respond in valid JSON", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesAgentPrompt()
    {
        var executor = CreateExecutor();
        var request = CreateRequest(prompt: "Analyze the codebase.");

        var prompt = executor.BuildPrompt(request);

        Assert.Contains("Analyze the codebase.", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesInputsAsJson()
    {
        var executor = CreateExecutor();
        var request = CreateRequest(inputs: new() { ["requirement"] = "Build API" });

        var prompt = executor.BuildPrompt(request);

        Assert.Contains("requirement", prompt);
        Assert.Contains("Build API", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesSkillInstructions()
    {
        var executor = CreateExecutor();
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "code_analysis",
                Type = "cognitive",
                Instructions = "Analyze code structure and patterns.",
                Constraints = ["Do not modify files"]
            }
        };
        var request = CreateRequest(skills: skills);

        var prompt = executor.BuildPrompt(request);

        Assert.Contains("code_analysis", prompt);
        Assert.Contains("Analyze code structure and patterns.", prompt);
        Assert.Contains("Do not modify files", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesToolDescriptions()
    {
        var executor = CreateExecutor();
        var tools = new List<ToolDefinition>
        {
            new()
            {
                Name = "read_files",
                Description = "Read file contents",
                Input = new() { ["path"] = "File path to read" }
            }
        };
        var request = CreateRequest(tools: tools);

        var prompt = executor.BuildPrompt(request);

        Assert.Contains("read_files", prompt);
        Assert.Contains("Read file contents", prompt);
        Assert.Contains("tool_calls", prompt);
    }

    [Fact]
    public void BuildPrompt_WithoutPrompts_StillBuildsValidPrompt()
    {
        var executor = CreateExecutor(prompts: []);
        var request = CreateRequest();

        var prompt = executor.BuildPrompt(request);

        Assert.Contains("Analyze the input.", prompt);
    }

    // --- Response parsing tests ---

    [Fact]
    public void ParseLlmResponse_ValidJson_ParsesCorrectly()
    {
        var executor = CreateExecutor();
        var json = """
        {
            "status": "success",
            "data": { "result": "analysis complete" },
            "message": "Done"
        }
        """;

        var parsed = executor.ParseLlmResponse(json);

        Assert.Equal("success", parsed.Status);
        Assert.Equal("Done", parsed.Message);
        Assert.NotNull(parsed.Data);
        Assert.Null(parsed.ToolCalls);
    }

    [Fact]
    public void ParseLlmResponse_WithToolCalls_ParsesToolCalls()
    {
        var executor = CreateExecutor();
        var json = """
        {
            "status": "success",
            "data": {},
            "message": "Need to read files",
            "tool_calls": [
                { "tool": "read_files", "inputs": { "path": "src/main.cs" } }
            ]
        }
        """;

        var parsed = executor.ParseLlmResponse(json);

        Assert.NotNull(parsed.ToolCalls);
        Assert.Single(parsed.ToolCalls);
        Assert.Equal("read_files", parsed.ToolCalls[0].Tool);
        Assert.Equal("src/main.cs", parsed.ToolCalls[0].Inputs["path"]);
    }

    [Fact]
    public void ParseLlmResponse_MarkdownCodeBlock_ExtractsJson()
    {
        var executor = CreateExecutor();
        var response = """
        ```json
        {
            "status": "success",
            "data": { "key": "value" },
            "message": "OK"
        }
        ```
        """;

        var parsed = executor.ParseLlmResponse(response);

        Assert.Equal("success", parsed.Status);
        Assert.Equal("OK", parsed.Message);
    }

    [Fact]
    public void ParseLlmResponse_InvalidJson_ReturnsFallbackWithRawResponse()
    {
        var executor = CreateExecutor();
        var response = "This is not JSON at all.";

        var parsed = executor.ParseLlmResponse(response);

        Assert.Equal("success", parsed.Status);
        Assert.NotNull(parsed.Data);
        Assert.True(parsed.Data.ContainsKey("raw_response"));
    }

    [Fact]
    public void ParseLlmResponse_MissingStatusField_DefaultsToSuccess()
    {
        var executor = CreateExecutor();
        var json = """{ "data": { "x": 1 } }""";

        var parsed = executor.ParseLlmResponse(json);

        Assert.Equal("success", parsed.Status);
    }

    // --- Execution tests ---

    [Fact]
    public async Task ExecuteAsync_SimpleSuccess_ReturnsAgentResult()
    {
        var llm = new FakeLlmClient("""
        {
            "status": "success",
            "data": { "analysis": "Project uses .NET 8" },
            "message": "Analysis complete"
        }
        """);

        var executor = CreateExecutor(llmClient: llm);
        var request = CreateRequest();

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal("Analysis complete", result.Message);
        Assert.True(result.Data.ContainsKey("analysis"));
        Assert.Equal(1, llm.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ErrorStatus_ReturnsErrorResult()
    {
        var llm = new FakeLlmClient("""
        {
            "status": "error",
            "data": {},
            "message": "Could not analyze"
        }
        """);

        var executor = CreateExecutor(llmClient: llm);
        var request = CreateRequest();

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal("Could not analyze", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithToolCalls_ExecutesToolsAndCallsLlmAgain()
    {
        var responses = new[]
        {
            // First response: request tool call
            """
            {
                "status": "success",
                "data": {},
                "message": "Need file contents",
                "tool_calls": [
                    { "tool": "read_files", "inputs": { "path": "src/main.cs" } }
                ]
            }
            """,
            // Second response: final answer
            """
            {
                "status": "success",
                "data": { "analysis": "File analyzed" },
                "message": "Done"
            }
            """
        };

        var llm = new SequentialLlmClient(responses);
        var toolExecutor = new FakeToolExecutor();
        var tools = new List<ToolDefinition>
        {
            new() { Name = "read_files", Description = "Read file" }
        };

        var executor = CreateExecutor(llmClient: llm, toolExecutor: toolExecutor);
        var request = CreateRequest(tools: tools);

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal("Done", result.Message);
        Assert.Equal(2, llm.CallCount);
        Assert.Equal(1, toolExecutor.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ToolCallWithNoToolsAssigned_ReturnsWithoutToolExecution()
    {
        var llm = new FakeLlmClient("""
        {
            "status": "success",
            "data": { "result": "partial" },
            "message": "Wanted tools but have none",
            "tool_calls": [
                { "tool": "read_files", "inputs": { "path": "x" } }
            ]
        }
        """);

        var toolExecutor = new FakeToolExecutor();
        var executor = CreateExecutor(llmClient: llm, toolExecutor: toolExecutor);
        var request = CreateRequest(tools: []); // no tools assigned

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal(0, toolExecutor.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ToolNotFound_ReturnsErrorInToolResult()
    {
        var responses = new[]
        {
            """
            {
                "status": "success",
                "data": {},
                "tool_calls": [
                    { "tool": "nonexistent_tool", "inputs": {} }
                ]
            }
            """,
            """
            {
                "status": "success",
                "data": { "recovery": true },
                "message": "Recovered"
            }
            """
        };

        var llm = new SequentialLlmClient(responses);
        var tools = new List<ToolDefinition>
        {
            new() { Name = "read_files", Description = "Read file" }
        };

        var executor = CreateExecutor(llmClient: llm);
        var request = CreateRequest(tools: tools);

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal(2, llm.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var llm = new FakeLlmClient("""{ "status": "success" }""");
        var executor = CreateExecutor(llmClient: llm);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(CreateRequest(), cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_PlainTextResponse_ReturnsFallbackResult()
    {
        var llm = new FakeLlmClient("This project uses .NET 8 with clean architecture.");
        var executor = CreateExecutor(llmClient: llm);
        var request = CreateRequest();

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.True(result.Data.ContainsKey("raw_response"));
    }

    // --- Test doubles ---

    private class FakeLlmClient : ILlmClient
    {
        private readonly string _response;
        public int CallCount { get; private set; }

        public FakeLlmClient(string response) => _response = response;

        public Task<string> CompleteAsync(string prompt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_response);
        }
    }

    private class SequentialLlmClient : ILlmClient
    {
        private readonly string[] _responses;
        public int CallCount { get; private set; }

        public SequentialLlmClient(string[] responses) => _responses = responses;

        public Task<string> CompleteAsync(string prompt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var index = Math.Min(CallCount, _responses.Length - 1);
            CallCount++;
            return Task.FromResult(_responses[index]);
        }
    }

    private class FakeToolExecutor : IToolExecutor
    {
        public int CallCount { get; private set; }

        public Task<ToolResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new ToolResult
            {
                Success = true,
                Output = new Dictionary<string, object> { ["result"] = $"Executed {request.Tool.Name}" }
            });
        }
    }
}
