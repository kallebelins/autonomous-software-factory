namespace AutonomousSoftwareFactory.Tests;

using AutonomousSoftwareFactory.Agents;
using AutonomousSoftwareFactory.Llm;
using AutonomousSoftwareFactory.Models;
using AutonomousSoftwareFactory.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Integration tests that use AgentExecutor with a real ToolExecutor (internal tools)
/// and a mock LLM to validate the full agent → tool → response loop.
/// </summary>
public class AgentToolIntegrationTests : IDisposable
{
    private readonly string _tempWorkspace;
    private readonly ToolExecutor _toolExecutor;
    private readonly List<PromptDefinition> _prompts;

    public AgentToolIntegrationTests()
    {
        _tempWorkspace = Path.Combine(Path.GetTempPath(), "agent-tool-integ-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempWorkspace);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workspace:BasePath"] = _tempWorkspace,
                ["GitHub:Token"] = "fake-token",
                ["GitHub:ApiUrl"] = "https://api.github.com"
            })
            .Build();

        _toolExecutor = new ToolExecutor(
            new FakeHttpClientFactory(),
            config,
            new NullLogger<ToolExecutor>());

        _prompts =
        [
            new() { Key = "system", Template = "You are a specialized agent." },
            new() { Key = "output_format", Template = "Respond in JSON with status, data, message." }
        ];
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempWorkspace))
            Directory.Delete(_tempWorkspace, recursive: true);
    }

    private AgentExecutor CreateExecutor(ILlmClient llmClient) =>
        new(llmClient, _toolExecutor, _prompts, new NullLogger<AgentExecutor>());

    // ────────────────────── read_files via tool call ──────────────────

    [Fact]
    public async Task Agent_ReadsFile_ViaToolCall_AndReturnsAnalysis()
    {
        // Arrange: place a file in the workspace
        File.WriteAllText(Path.Combine(_tempWorkspace, "readme.md"), "# My Project\nA sample project.");

        var llm = new SequentialFakeLlm(
            // Round 1: agent requests to read the file
            """
            {
                "status": "success",
                "data": {},
                "message": "Reading file",
                "tool_calls": [
                    { "tool": "read_files", "inputs": { "path": "readme.md" } }
                ]
            }
            """,
            // Round 2: agent returns final analysis after seeing tool results
            """
            {
                "status": "success",
                "data": { "summary": "Project readme describes a sample project" },
                "message": "Analysis complete"
            }
            """);

        var executor = CreateExecutor(llm);
        var request = new AgentExecutionRequest
        {
            Agent = new AgentDefinition { Name = "analyzer" },
            Inputs = new() { ["task"] = "Analyze readme" },
            Skills = [],
            Tools = [new ToolDefinition { Name = "read_files", ExecutionType = "internal", Description = "Read file" }],
            Prompt = "Analyze the readme file.",
            Context = new ExecutionContext()
        };

        // Act
        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal("success", result.Status);
        Assert.Equal("Analysis complete", result.Message);
        Assert.True(result.Data.ContainsKey("summary"));
        Assert.Equal(2, llm.CallCount);

        // Verify that the second prompt contains tool results with actual file content
        Assert.Contains("# My Project", llm.ReceivedPrompts[1]);
        Assert.Contains("Tool Execution Results", llm.ReceivedPrompts[1]);
    }

    // ────────────────────── write_file + read_files loop ─────────────

    [Fact]
    public async Task Agent_WritesFile_ThenReadsItBack()
    {
        var llm = new SequentialFakeLlm(
            // Round 1: agent requests to create a file
            """
            {
                "status": "success",
                "data": {},
                "message": "Creating file",
                "tool_calls": [
                    { "tool": "create_file", "inputs": { "path": "output.cs", "content": "public class Foo {}" } }
                ]
            }
            """,
            // Round 2: agent requests to read back the file
            """
            {
                "status": "success",
                "data": {},
                "message": "Verifying",
                "tool_calls": [
                    { "tool": "read_files", "inputs": { "path": "output.cs" } }
                ]
            }
            """,
            // Round 3: final answer
            """
            {
                "status": "success",
                "data": { "file_created": true, "verified": true },
                "message": "File created and verified"
            }
            """);

        var executor = CreateExecutor(llm);
        var request = new AgentExecutionRequest
        {
            Agent = new AgentDefinition { Name = "code_generator" },
            Inputs = new(),
            Skills = [],
            Tools =
            [
                new ToolDefinition { Name = "create_file", ExecutionType = "internal", Description = "Create file" },
                new ToolDefinition { Name = "read_files", ExecutionType = "internal", Description = "Read file" }
            ],
            Prompt = "Generate a class file.",
            Context = new ExecutionContext()
        };

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal("File created and verified", result.Message);
        Assert.Equal(3, llm.CallCount);

        // Verify the file was actually created on disk
        var filePath = Path.Combine(_tempWorkspace, "output.cs");
        Assert.True(File.Exists(filePath));
        Assert.Equal("public class Foo {}", File.ReadAllText(filePath));

        // Verify second prompt contains create_file success result
        Assert.Contains("Success: True", llm.ReceivedPrompts[1]);
    }

    // ────────────────────── tool not found in available tools ─────────

    [Fact]
    public async Task Agent_RequestsUnknownTool_GetsErrorFeedback_AndRecovers()
    {
        var llm = new SequentialFakeLlm(
            // Round 1: agent requests a tool that's not in its assigned list
            """
            {
                "status": "success",
                "data": {},
                "tool_calls": [
                    { "tool": "delete_file", "inputs": { "path": "x.txt" } }
                ]
            }
            """,
            // Round 2: agent recovers after seeing the error
            """
            {
                "status": "success",
                "data": { "fallback": true },
                "message": "Recovered without tool"
            }
            """);

        var executor = CreateExecutor(llm);
        var request = new AgentExecutionRequest
        {
            Agent = new AgentDefinition { Name = "agent_limited" },
            Inputs = new(),
            Skills = [],
            Tools =
            [
                new ToolDefinition { Name = "read_files", ExecutionType = "internal", Description = "Read file" }
            ],
            Prompt = "Work with files.",
            Context = new ExecutionContext()
        };

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal("Recovered without tool", result.Message);
        Assert.Equal(2, llm.CallCount);

        // The error feedback should be in the second prompt
        Assert.Contains("not found", llm.ReceivedPrompts[1]);
    }

    // ────────────────────── list_directory via tool call ──────────────

    [Fact]
    public async Task Agent_ListsDirectory_AndReturnsStructure()
    {
        // Arrange workspace structure
        Directory.CreateDirectory(Path.Combine(_tempWorkspace, "src"));
        File.WriteAllText(Path.Combine(_tempWorkspace, "src", "Main.cs"), "");
        File.WriteAllText(Path.Combine(_tempWorkspace, "README.md"), "");

        var llm = new SequentialFakeLlm(
            """
            {
                "status": "success",
                "data": {},
                "tool_calls": [
                    { "tool": "list_directory", "inputs": { "path": "." } }
                ]
            }
            """,
            """
            {
                "status": "success",
                "data": { "structure": ["src/", "README.md"] },
                "message": "Structure mapped"
            }
            """);

        var executor = CreateExecutor(llm);
        var request = new AgentExecutionRequest
        {
            Agent = new AgentDefinition { Name = "mapper" },
            Inputs = new(),
            Skills = [],
            Tools =
            [
                new ToolDefinition { Name = "list_directory", ExecutionType = "internal", Description = "List dir" }
            ],
            Prompt = "Map the project structure.",
            Context = new ExecutionContext()
        };

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal("Structure mapped", result.Message);

        // Tool results should include actual directory contents
        Assert.Contains("src", llm.ReceivedPrompts[1]);
        Assert.Contains("README.md", llm.ReceivedPrompts[1]);
    }

    // ────────────────────── no tool call needed ──────────────────────

    [Fact]
    public async Task Agent_NoToolCalls_ReturnsDirectly()
    {
        var llm = new SequentialFakeLlm(
            """
            {
                "status": "success",
                "data": { "answer": "42" },
                "message": "Computed directly"
            }
            """);

        var executor = CreateExecutor(llm);
        var request = new AgentExecutionRequest
        {
            Agent = new AgentDefinition { Name = "thinker" },
            Inputs = new() { ["question"] = "What is the answer?" },
            Skills = [],
            Tools =
            [
                new ToolDefinition { Name = "read_files", ExecutionType = "internal", Description = "Read file" }
            ],
            Prompt = "Answer the question.",
            Context = new ExecutionContext()
        };

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal("Computed directly", result.Message);
        Assert.Equal(1, llm.CallCount);
    }

    // ───────────────────── Test doubles ──────────────────────────────

    private class SequentialFakeLlm : ILlmClient
    {
        private readonly string[] _responses;
        public int CallCount { get; private set; }
        public List<string> ReceivedPrompts { get; } = [];

        public SequentialFakeLlm(params string[] responses) => _responses = responses;

        public Task<string> CompleteAsync(string prompt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ReceivedPrompts.Add(prompt);
            var index = Math.Min(CallCount, _responses.Length - 1);
            CallCount++;
            return Task.FromResult(_responses[index]);
        }
    }

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
