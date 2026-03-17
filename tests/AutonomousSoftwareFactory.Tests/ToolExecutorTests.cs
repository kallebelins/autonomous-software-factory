namespace AutonomousSoftwareFactory.Tests;

using AutonomousSoftwareFactory.Models;
using AutonomousSoftwareFactory.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

public class ToolExecutorTests : IDisposable
{
    private readonly string _tempWorkspace;
    private readonly ToolExecutor _executor;

    public ToolExecutorTests()
    {
        _tempWorkspace = Path.Combine(Path.GetTempPath(), "tool-executor-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempWorkspace);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workspace:BasePath"] = _tempWorkspace,
                ["GitHub:Token"] = "fake-token",
                ["GitHub:ApiUrl"] = "https://api.github.com"
            })
            .Build();

        var httpFactory = new FakeHttpClientFactory();

        _executor = new ToolExecutor(
            httpFactory,
            config,
            new NullLogger<ToolExecutor>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempWorkspace))
            Directory.Delete(_tempWorkspace, recursive: true);
    }

    // ───────────────────────────── internal: read_files ────────────────

    [Fact]
    public async Task ReadFiles_ExistingFile_ReturnsContent()
    {
        File.WriteAllText(Path.Combine(_tempWorkspace, "hello.txt"), "Hello World");

        var result = await Execute("read_files", "internal", inputs: new() { ["path"] = "hello.txt" });

        Assert.True(result.Success);
        Assert.Equal("Hello World", result.Output["content"]);
    }

    [Fact]
    public async Task ReadFiles_NonExistentFile_Fails()
    {
        var result = await Execute("read_files", "internal", inputs: new() { ["path"] = "missing.txt" });

        Assert.False(result.Success);
        Assert.Contains("not found", result.Errors[0]);
    }

    [Fact]
    public async Task ReadFiles_PathTraversal_Blocked()
    {
        var result = await Execute("read_files", "internal", inputs: new() { ["path"] = "../../etc/passwd" });

        Assert.False(result.Success);
        Assert.Contains("outside the workspace", result.Errors[0]);
    }

    // ───────────────────────────── internal: list_directory ────────────

    [Fact]
    public async Task ListDirectory_ReturnsEntries()
    {
        Directory.CreateDirectory(Path.Combine(_tempWorkspace, "subdir"));
        File.WriteAllText(Path.Combine(_tempWorkspace, "file1.txt"), "");

        var result = await Execute("list_directory", "internal", inputs: new() { ["path"] = "." });

        Assert.True(result.Success);
        var files = result.Output["files"] as List<string>;
        Assert.NotNull(files);
        Assert.Contains("subdir", files);
        Assert.Contains("file1.txt", files);
    }

    [Fact]
    public async Task ListDirectory_NonExistent_Fails()
    {
        var result = await Execute("list_directory", "internal", inputs: new() { ["path"] = "nope" });

        Assert.False(result.Success);
        Assert.Contains("not found", result.Errors[0]);
    }

    // ───────────────────────────── internal: write_file ────────────────

    [Fact]
    public async Task WriteFile_CreatesAndWritesContent()
    {
        var result = await Execute("write_file", "internal", inputs: new()
        {
            ["path"] = "output.txt",
            ["content"] = "written content"
        });

        Assert.True(result.Success);
        Assert.Equal("written content", File.ReadAllText(Path.Combine(_tempWorkspace, "output.txt")));
    }

    [Fact]
    public async Task WriteFile_CreatesIntermediateDirectories()
    {
        var result = await Execute("write_file", "internal", inputs: new()
        {
            ["path"] = "deep/nested/dir/file.txt",
            ["content"] = "deep content"
        });

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_tempWorkspace, "deep", "nested", "dir", "file.txt")));
    }

    [Fact]
    public async Task WriteFile_MissingContent_Fails()
    {
        var result = await Execute("write_file", "internal", inputs: new() { ["path"] = "x.txt" });

        Assert.False(result.Success);
        Assert.Contains("content", result.Errors[0]);
    }

    // ───────────────────────────── internal: create_file ───────────────

    [Fact]
    public async Task CreateFile_NewFile_Succeeds()
    {
        var result = await Execute("create_file", "internal", inputs: new()
        {
            ["path"] = "new-file.cs",
            ["content"] = "// new"
        });

        Assert.True(result.Success);
        Assert.Equal("// new", File.ReadAllText(Path.Combine(_tempWorkspace, "new-file.cs")));
    }

    [Fact]
    public async Task CreateFile_AlreadyExists_Fails()
    {
        File.WriteAllText(Path.Combine(_tempWorkspace, "existing.txt"), "old");

        var result = await Execute("create_file", "internal", inputs: new()
        {
            ["path"] = "existing.txt",
            ["content"] = "new"
        });

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Errors[0]);
        Assert.Equal("old", File.ReadAllText(Path.Combine(_tempWorkspace, "existing.txt")));
    }

    // ───────────────────────────── internal: delete_file ───────────────

    [Fact]
    public async Task DeleteFile_ExistingFile_Succeeds()
    {
        var filePath = Path.Combine(_tempWorkspace, "to-delete.txt");
        File.WriteAllText(filePath, "bye");

        var result = await Execute("delete_file", "internal", inputs: new() { ["path"] = "to-delete.txt" });

        Assert.True(result.Success);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteFile_NonExistent_Fails()
    {
        var result = await Execute("delete_file", "internal", inputs: new() { ["path"] = "ghost.txt" });

        Assert.False(result.Success);
        Assert.Contains("not found", result.Errors[0]);
    }

    // ───────────────────────────── internal: search_files ──────────────

    [Fact]
    public async Task SearchFiles_FindsMatchingText()
    {
        File.WriteAllText(Path.Combine(_tempWorkspace, "code.cs"), "public class MyService\n{\n}\n");

        var result = await Execute("search_files", "internal", inputs: new()
        {
            ["pattern"] = "MyService",
            ["path"] = "."
        });

        Assert.True(result.Success);
        var matches = result.Output["matches"] as List<object>;
        Assert.NotNull(matches);
        Assert.NotEmpty(matches);
    }

    [Fact]
    public async Task SearchFiles_NoMatches_ReturnsEmptyList()
    {
        File.WriteAllText(Path.Combine(_tempWorkspace, "code.cs"), "public class Foo {}");

        var result = await Execute("search_files", "internal", inputs: new()
        {
            ["pattern"] = "NONEXISTENT_TEXT_12345",
            ["path"] = "."
        });

        Assert.True(result.Success);
        var matches = result.Output["matches"] as List<object>;
        Assert.NotNull(matches);
        Assert.Empty(matches);
    }

    // ───────────────────────────── command execution ───────────────────

    [Fact]
    public async Task Command_EchoCommand_ReturnsStdout()
    {
        var tool = new ToolDefinition
        {
            Name = "test_echo",
            ExecutionType = "command",
            Command = OperatingSystem.IsWindows() ? "echo hello" : "echo hello"
        };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = new(),
            WorkingDirectory = _tempWorkspace
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("hello", result.Output["stdout"]?.ToString());
        Assert.Equal(0, result.Output["exit_code"]);
    }

    [Fact]
    public async Task Command_PlaceholderSubstitution_Works()
    {
        var fileName = "placeholder-test.txt";
        File.WriteAllText(Path.Combine(_tempWorkspace, fileName), "test content");

        var tool = new ToolDefinition
        {
            Name = "test_type",
            ExecutionType = "command",
            Command = OperatingSystem.IsWindows()
                ? "type {{filename}}"
                : "cat {{filename}}"
        };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = new() { ["filename"] = fileName },
            WorkingDirectory = _tempWorkspace
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("test content", result.Output["stdout"]?.ToString());
    }

    [Fact]
    public async Task Command_FailingCommand_ReturnsFalseWithError()
    {
        var tool = new ToolDefinition
        {
            Name = "test_fail",
            ExecutionType = "command",
            Command = OperatingSystem.IsWindows()
                ? "exit /b 1"
                : "exit 1"
        };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = new(),
            WorkingDirectory = _tempWorkspace
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.NotEqual(0, result.Output["exit_code"]);
    }

    [Fact]
    public async Task Command_NoTemplate_Fails()
    {
        var tool = new ToolDefinition
        {
            Name = "no_cmd",
            ExecutionType = "command",
            Command = null
        };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = new(),
            WorkingDirectory = _tempWorkspace
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("no command template", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    // ───────────────────────────── stack detection ─────────────────────

    [Fact]
    public void DetectStack_Dotnet_WhenCsprojExists()
    {
        File.WriteAllText(Path.Combine(_tempWorkspace, "App.csproj"), "<Project/>");

        Assert.Equal("dotnet", ToolExecutor.DetectStack(_tempWorkspace));
    }

    [Fact]
    public void DetectStack_Maven_WhenPomXmlExists()
    {
        File.WriteAllText(Path.Combine(_tempWorkspace, "pom.xml"), "<project/>");

        Assert.Equal("maven", ToolExecutor.DetectStack(_tempWorkspace));
    }

    [Fact]
    public void DetectStack_Npm_WhenPackageJsonExists()
    {
        File.WriteAllText(Path.Combine(_tempWorkspace, "package.json"), "{}");

        Assert.Equal("npm", ToolExecutor.DetectStack(_tempWorkspace));
    }

    [Fact]
    public void DetectStack_Unknown_WhenNothingMatches()
    {
        Assert.Equal("unknown", ToolExecutor.DetectStack(_tempWorkspace));
    }

    // ───────────────────────────── unknown execution type ─────────────

    [Fact]
    public async Task UnknownExecutionType_Fails()
    {
        var tool = new ToolDefinition { Name = "weird", ExecutionType = "magic" };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = new()
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Unknown execution_type", result.Errors[0]);
    }

    // ───────────────────────────── unknown internal tool ───────────────

    [Fact]
    public async Task UnknownInternalTool_Fails()
    {
        var tool = new ToolDefinition { Name = "nonexistent_internal", ExecutionType = "internal" };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = new()
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Unknown internal tool", result.Errors[0]);
    }

    // ───────────────────────────── path safety ─────────────────────────

    [Fact]
    public void ResolveSafePath_ValidRelative_ReturnsFullPath()
    {
        var resolved = _executor.ResolveSafePath("subdir/file.txt");

        Assert.NotNull(resolved);
        Assert.StartsWith(_tempWorkspace, resolved);
    }

    [Fact]
    public void ResolveSafePath_TraversalAttempt_ReturnsNull()
    {
        var resolved = _executor.ResolveSafePath("../../etc/passwd");

        Assert.Null(resolved);
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private Task<ToolResult> Execute(string toolName, string executionType,
        Dictionary<string, string> inputs)
    {
        var tool = new ToolDefinition
        {
            Name = toolName,
            ExecutionType = executionType
        };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = inputs,
            WorkingDirectory = _tempWorkspace
        };

        return _executor.ExecuteAsync(request, CancellationToken.None);
    }

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
