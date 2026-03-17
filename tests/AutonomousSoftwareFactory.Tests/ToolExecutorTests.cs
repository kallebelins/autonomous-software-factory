namespace AutonomousSoftwareFactory.Tests;

using System.Diagnostics;
using System.Net;
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
        {
            // Git creates read-only files in .git/objects/ — reset attributes before deleting
            foreach (var file in Directory.EnumerateFiles(_tempWorkspace, "*", SearchOption.AllDirectories))
            {
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
            Directory.Delete(_tempWorkspace, recursive: true);
        }
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

    // ───────────────────────────── build: dotnet ────────────────────────

    [Fact]
    public async Task DotnetBuild_Success_ReturnsZeroExitCode()
    {
        var result = await ExecuteBuildCommand("dotnet_build", "build",
            OperatingSystem.IsWindows() ? "echo Build succeeded." : "echo 'Build succeeded.'");

        Assert.True(result.Success);
        Assert.Contains("Build succeeded", result.Output["stdout"]?.ToString());
    }

    [Fact]
    public async Task DotnetBuild_Failure_ReturnsFalse()
    {
        var result = await ExecuteBuildCommand("dotnet_build", "build",
            OperatingSystem.IsWindows() ? "echo Build FAILED. 1>&2 & exit /b 1" : "echo 'Build FAILED.' >&2; exit 1");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task DotnetRestore_Success_ReturnsTrue()
    {
        var result = await ExecuteBuildCommand("dotnet_restore", "dependency",
            OperatingSystem.IsWindows() ? "echo Restore completed." : "echo 'Restore completed.'");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task DotnetTest_ParsesTestCounts()
    {
        // Simulate dotnet test output
        var cmd = OperatingSystem.IsWindows()
            ? "echo Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5"
            : "echo 'Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5'";

        var result = await ExecuteBuildCommand("dotnet_test", "test", cmd);

        Assert.True(result.Success);
        Assert.Equal(5, result.Output["total"]);
        Assert.Equal(5, result.Output["passed"]);
        Assert.Equal(0, result.Output["failed"]);
    }

    [Fact]
    public async Task DotnetTest_WithFailures_ParsesCounts()
    {
        var cmd = OperatingSystem.IsWindows()
            ? "echo Failed!  - Failed:     2, Passed:     3, Skipped:     0, Total:     5 & exit /b 1"
            : "echo 'Failed!  - Failed:     2, Passed:     3, Skipped:     0, Total:     5'; exit 1";

        var result = await ExecuteBuildCommand("dotnet_test", "test", cmd);

        Assert.False(result.Success);
        Assert.Equal(5, result.Output["total"]);
        Assert.Equal(3, result.Output["passed"]);
        Assert.Equal(2, result.Output["failed"]);
    }

    // ───────────────────────────── build: maven ───────────────────────

    [Fact]
    public async Task MavenBuild_Success_ReturnsZeroExitCode()
    {
        var result = await ExecuteBuildCommand("maven_build", "build",
            OperatingSystem.IsWindows() ? "echo BUILD SUCCESS" : "echo 'BUILD SUCCESS'");

        Assert.True(result.Success);
        Assert.Contains("BUILD SUCCESS", result.Output["stdout"]?.ToString());
    }

    [Fact]
    public async Task JunitTest_ParsesTestCounts()
    {
        var cmd = OperatingSystem.IsWindows()
            ? "echo Tests run: 10, Failures: 1, Errors: 0, Skipped: 2"
            : "echo 'Tests run: 10, Failures: 1, Errors: 0, Skipped: 2'";

        var result = await ExecuteBuildCommand("junit_test", "test", cmd);

        Assert.True(result.Success);
        Assert.Equal(10, result.Output["total"]);
        Assert.Equal(9, result.Output["passed"]);
        Assert.Equal(1, result.Output["failed"]);
    }

    [Fact]
    public async Task MavenInstall_Success_ReturnsTrue()
    {
        var result = await ExecuteBuildCommand("maven_install", "dependency",
            OperatingSystem.IsWindows() ? "echo BUILD SUCCESS" : "echo 'BUILD SUCCESS'");

        Assert.True(result.Success);
    }

    // ───────────────────────────── build: npm ─────────────────────────

    [Fact]
    public async Task NpmBuild_Success_ReturnsZeroExitCode()
    {
        var result = await ExecuteBuildCommand("npm_build", "build",
            OperatingSystem.IsWindows() ? "echo compiled successfully" : "echo 'compiled successfully'");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task JestTest_ParsesTestCounts()
    {
        var cmd = OperatingSystem.IsWindows()
            ? "echo Tests:       1 failed, 3 passed, 4 total"
            : "echo 'Tests:       1 failed, 3 passed, 4 total'";

        var result = await ExecuteBuildCommand("jest_test", "test", cmd);

        Assert.True(result.Success);
        Assert.Equal(4, result.Output["total"]);
        Assert.Equal(3, result.Output["passed"]);
        Assert.Equal(1, result.Output["failed"]);
    }

    [Fact]
    public async Task JestTest_AllPassed_ParsesCorrectly()
    {
        var cmd = OperatingSystem.IsWindows()
            ? "echo Tests:       5 passed, 5 total"
            : "echo 'Tests:       5 passed, 5 total'";

        var result = await ExecuteBuildCommand("jest_test", "test", cmd);

        Assert.True(result.Success);
        Assert.Equal(5, result.Output["total"]);
        Assert.Equal(5, result.Output["passed"]);
        Assert.Equal(0, result.Output["failed"]);
    }

    [Fact]
    public async Task NpmInstall_Success_ReturnsTrue()
    {
        var result = await ExecuteBuildCommand("npm_install", "dependency",
            OperatingSystem.IsWindows() ? "echo added 100 packages" : "echo 'added 100 packages'");

        Assert.True(result.Success);
    }

    // ───────────────────────────── quality / lint ──────────────────────

    [Fact]
    public async Task DotnetFormat_Success_NoIssues()
    {
        var result = await ExecuteBuildCommand("dotnet_format", "quality",
            OperatingSystem.IsWindows() ? "echo Format complete." : "echo 'Format complete.'");

        Assert.True(result.Success);
        var issues = result.Output["issues"] as List<string>;
        Assert.NotNull(issues);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Eslint_WithProblems_ParsesOutput()
    {
        var cmd = OperatingSystem.IsWindows()
            ? "echo 5 problems (3 errors, 2 warnings)"
            : "echo '5 problems (3 errors, 2 warnings)'";

        var result = await ExecuteBuildCommand("eslint", "quality", cmd);

        Assert.True(result.Success);
        var issues = result.Output["issues"] as List<string>;
        Assert.NotNull(issues);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public async Task Checkstyle_WithErrors_ParsesOutput()
    {
        var cmd = OperatingSystem.IsWindows()
            ? "echo [ERROR] src/Main.java:10: Missing Javadoc"
            : "echo '[ERROR] src/Main.java:10: Missing Javadoc'";

        var result = await ExecuteBuildCommand("checkstyle", "quality", cmd);

        Assert.True(result.Success);
        var issues = result.Output["issues"] as List<string>;
        Assert.NotNull(issues);
        Assert.NotEmpty(issues);
    }

    // ───────────────────────────── git: helper methods ───────────────

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "repo")]
    [InlineData("https://github.com/owner/repo", "repo")]
    [InlineData("https://github.com/owner/my-project.git", "my-project")]
    [InlineData("git@github.com:owner/repo.git", "repo")]
    [InlineData("https://github.com/owner/repo/", "repo")]
    public void ExtractRepoNameFromUrl_VariousFormats(string url, string expected)
    {
        Assert.Equal(expected, ToolExecutor.ExtractRepoNameFromUrl(url));
    }

    [Fact]
    public void InjectTokenInGitUrl_InjectsToken()
    {
        var result = ToolExecutor.InjectTokenInGitUrl("https://github.com/owner/repo.git", "my-token");
        Assert.Equal("https://x-access-token:my-token@github.com/owner/repo.git", result);
    }

    [Fact]
    public void InjectTokenInGitUrl_DoesNotDoubleInject()
    {
        var url = "https://x-access-token:existing@github.com/owner/repo.git";
        Assert.Equal(url, ToolExecutor.InjectTokenInGitUrl(url, "new-token"));
    }

    [Fact]
    public void InjectTokenInGitUrl_SshUrl_Unchanged()
    {
        var url = "git@github.com:owner/repo.git";
        Assert.Equal(url, ToolExecutor.InjectTokenInGitUrl(url, "token"));
    }

    // ───────────────────────────── git: clone validation ──────────────

    [Fact]
    public async Task GitClone_MissingRepoUrl_Fails()
    {
        var result = await ExecuteGitCommand("git_clone",
            "git clone -b {{branch}} {{repo_url}} {{destination}}",
            new() { ["branch"] = "main" });

        Assert.False(result.Success);
        Assert.Contains("repo_url", result.Errors[0]);
    }

    [Fact]
    public async Task GitClone_DestinationExists_Fails()
    {
        var dest = Path.Combine(_tempWorkspace, "repos", "existing-repo");
        Directory.CreateDirectory(dest);

        var result = await ExecuteGitCommand("git_clone",
            "git clone -b {{branch}} {{repo_url}} {{destination}}",
            new()
            {
                ["repo_url"] = "https://github.com/owner/existing-repo.git",
                ["branch"] = "main",
                ["destination"] = "existing-repo"
            });

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Errors[0]);
    }

    [Fact]
    public async Task GitClone_DefaultsDestinationFromUrl()
    {
        // Use echo to see the resolved destination instead of real git clone
        var result = await ExecuteGitCommand("git_clone",
            OperatingSystem.IsWindows()
                ? "echo {{destination}}"
                : "echo '{{destination}}'",
            new()
            {
                ["repo_url"] = "https://github.com/owner/my-repo.git",
                ["branch"] = "main"
            });

        var expectedDest = Path.Combine(_tempWorkspace, "repos", "my-repo");
        Assert.Contains(expectedDest, result.Output["stdout"]?.ToString() ?? "");
    }

    [Fact]
    public async Task GitClone_DefaultsBranchToMain()
    {
        var result = await ExecuteGitCommand("git_clone",
            OperatingSystem.IsWindows()
                ? "echo {{branch}}"
                : "echo '{{branch}}'",
            new()
            {
                ["repo_url"] = "https://github.com/owner/test-repo.git"
            });

        Assert.Contains("main", result.Output["stdout"]?.ToString() ?? "");
    }

    // ───────────────────────────── git: commit validation ─────────────

    [Fact]
    public async Task GitCommit_MissingMessage_Fails()
    {
        var result = await ExecuteGitCommand("git_commit",
            "git add . && git commit -m '{{message}}'",
            new() { ["working_directory"] = _tempWorkspace });

        Assert.False(result.Success);
        Assert.Contains("message", result.Errors[0]);
    }

    [Fact]
    public async Task GitCommit_EmptyMessage_Fails()
    {
        var result = await ExecuteGitCommand("git_commit",
            "git add . && git commit -m '{{message}}'",
            new() { ["message"] = "   ", ["working_directory"] = _tempWorkspace });

        Assert.False(result.Success);
        Assert.Contains("message", result.Errors[0]);
    }

    // ───────────────────────────── git: create_pull_request ───────────

    [Fact]
    public async Task CreatePullRequest_MissingToken_Fails()
    {
        // Create executor WITHOUT a GitHub token
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workspace:BasePath"] = _tempWorkspace,
                ["GitHub:Token"] = "",
                ["GitHub:ApiUrl"] = "https://api.github.com"
            })
            .Build();

        var executor = new ToolExecutor(
            new FakeHttpClientFactory(),
            config,
            new NullLogger<ToolExecutor>());

        var tool = new ToolDefinition
        {
            Name = "create_pull_request",
            ExecutionType = "api",
            Api = new ApiDefinition
            {
                Provider = "github",
                Endpoint = "/repos/{{owner}}/{{repo}}/pulls",
                Method = "POST",
                Headers = new()
                {
                    ["Authorization"] = "Bearer {{secrets.github_token}}",
                    ["Accept"] = "application/vnd.github+json"
                },
                Body = new()
                {
                    ["title"] = "{{title}}",
                    ["body"] = "{{description}}",
                    ["head"] = "{{branch}}",
                    ["base"] = "{{base_branch}}"
                }
            }
        };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = new()
            {
                ["title"] = "Test PR",
                ["description"] = "Test description",
                ["branch"] = "feature/test",
                ["base_branch"] = "main",
                ["owner"] = "owner",
                ["repo"] = "repo"
            }
        };

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("GitHub token is not configured", result.Errors[0]);
    }

    // ───────────────────────────── git: integration ───────────────────

    [Fact]
    public async Task GitWorkflow_CloneBranchCommitPush_LocalRepo()
    {
        // 1. Create a bare repo (acts as remote)
        var bareRepoPath = Path.Combine(_tempWorkspace, "bare-repo.git");
        await RunGit($"init --bare \"{bareRepoPath}\"", _tempWorkspace);

        // 2. Initialize a seed repo with a commit
        var seedPath = Path.Combine(_tempWorkspace, "seed-repo");
        Directory.CreateDirectory(seedPath);
        await RunGit("init -b main", seedPath);
        await RunGit("config user.email test@test.com", seedPath);
        await RunGit("config user.name Test", seedPath);
        File.WriteAllText(Path.Combine(seedPath, "README.md"), "# Test Repo");
        await RunGit("add .", seedPath);
        await RunGit("commit -m \"Initial commit\"", seedPath);
        await RunGit($"remote add origin \"{bareRepoPath}\"", seedPath);
        await RunGit("push origin main", seedPath);

        // 3. Clone using our ToolExecutor (local path, no token injection)
        var cloneDest = Path.Combine(_tempWorkspace, "repos", "cloned-repo");
        var cloneResult = await ExecuteGitCommand("git_clone",
            "git clone -b {{branch}} {{repo_url}} {{destination}}",
            new()
            {
                ["repo_url"] = bareRepoPath,
                ["branch"] = "main",
                ["destination"] = cloneDest
            });

        Assert.True(cloneResult.Success, $"Clone failed: {string.Join("; ", cloneResult.Errors)}");
        Assert.True(Directory.Exists(cloneDest));
        Assert.Equal(cloneDest, cloneResult.Output["local_path"]?.ToString());

        // Configure git user for the cloned repo
        await RunGit("config user.email test@test.com", cloneDest);
        await RunGit("config user.name Test", cloneDest);

        // 4. Create branch
        var branchResult = await ExecuteGitCommand("git_branch",
            "git checkout -b {{branch_name}} {{base_branch}}",
            new()
            {
                ["branch_name"] = "feature/test",
                ["base_branch"] = "main",
                ["working_directory"] = cloneDest
            });

        Assert.True(branchResult.Success, $"Branch failed: {string.Join("; ", branchResult.Errors)}");

        // 5. Create a file, add and commit
        File.WriteAllText(Path.Combine(cloneDest, "new-file.txt"), "Hello World");
        var commitResult = await ExecuteGitCommand("git_commit",
            "git add . && git commit -m '{{message}}'",
            new()
            {
                ["message"] = "Add new file",
                ["working_directory"] = cloneDest
            });

        Assert.True(commitResult.Success, $"Commit failed: {string.Join("; ", commitResult.Errors)}");

        // 6. Push
        var pushResult = await ExecuteGitCommand("git_push",
            "git push origin {{branch_name}}",
            new()
            {
                ["branch_name"] = "feature/test",
                ["working_directory"] = cloneDest
            });

        Assert.True(pushResult.Success, $"Push failed: {string.Join("; ", pushResult.Errors)}");
    }

    // ───────────────────────────── git: clone local repo (standalone) ──

    [Fact]
    public async Task GitClone_LocalRepo_ClonesSuccessfully()
    {
        // Create a bare repo (acts as remote)
        var bareRepoPath = Path.Combine(_tempWorkspace, "remote.git");
        await RunGit($"init --bare \"{bareRepoPath}\"", _tempWorkspace);

        // Seed with initial commit
        var seedPath = Path.Combine(_tempWorkspace, "seed");
        Directory.CreateDirectory(seedPath);
        await RunGit("init -b main", seedPath);
        await RunGit("config user.email test@test.com", seedPath);
        await RunGit("config user.name Test", seedPath);
        File.WriteAllText(Path.Combine(seedPath, "README.md"), "# Test Repo");
        await RunGit("add .", seedPath);
        await RunGit("commit -m \"init\"", seedPath);
        await RunGit($"remote add origin \"{bareRepoPath}\"", seedPath);
        await RunGit("push origin main", seedPath);

        // Clone using ToolExecutor
        var cloneDest = Path.Combine(_tempWorkspace, "repos", "cloned");
        var result = await ExecuteGitCommand("git_clone",
            "git clone -b {{branch}} {{repo_url}} {{destination}}",
            new()
            {
                ["repo_url"] = bareRepoPath,
                ["branch"] = "main",
                ["destination"] = cloneDest
            });

        Assert.True(result.Success, $"Clone failed: {string.Join("; ", result.Errors)}");
        Assert.True(Directory.Exists(cloneDest));
        Assert.True(File.Exists(Path.Combine(cloneDest, "README.md")));
        Assert.Equal("# Test Repo", File.ReadAllText(Path.Combine(cloneDest, "README.md")));
        Assert.Equal(cloneDest, result.Output["local_path"]?.ToString());
    }

    // ───────────────────────── create_pull_request: mock API ──────────

    [Fact]
    public async Task CreatePullRequest_MockApi_ReturnsSuccess()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"number": 42, "html_url": "https://github.com/owner/my-repo/pull/42"}""",
                    System.Text.Encoding.UTF8, "application/json")
            });

        var executor = CreateExecutorWithHttpHandler(handler);

        var tool = new ToolDefinition
        {
            Name = "create_pull_request",
            ExecutionType = "api",
            Api = new ApiDefinition
            {
                Provider = "github",
                Endpoint = "/repos/{{owner}}/{{repo}}/pulls",
                Method = "POST",
                Headers = new()
                {
                    ["Authorization"] = "Bearer {{secrets.github_token}}",
                    ["Accept"] = "application/vnd.github+json"
                },
                Body = new()
                {
                    ["title"] = "{{title}}",
                    ["body"] = "{{description}}",
                    ["head"] = "{{branch}}",
                    ["base"] = "{{base_branch}}"
                }
            }
        };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = new()
            {
                ["owner"] = "owner",
                ["repo"] = "my-repo",
                ["title"] = "feat: new feature",
                ["description"] = "Adds a new feature",
                ["branch"] = "feature/new",
                ["base_branch"] = "main"
            }
        };

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(201, result.Output["status_code"]);
        Assert.Contains("42", result.Output["body"]?.ToString() ?? "");

        // Verify request was sent correctly
        var capturedReq = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, capturedReq.Method);
        Assert.Contains("/repos/owner/my-repo/pulls", capturedReq.RequestUri!.ToString());
        Assert.Equal("Bearer fake-token", capturedReq.Headers.Authorization?.ToString());

        var body = handler.LastRequestBody!;
        Assert.Contains("feat: new feature", body);
        Assert.Contains("feature/new", body);
    }

    [Fact]
    public async Task CreatePullRequest_MockApi_HandlesApiError()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    """{"message": "Validation Failed", "errors": [{"message": "A pull request already exists"}]}""",
                    System.Text.Encoding.UTF8, "application/json")
            });

        var executor = CreateExecutorWithHttpHandler(handler);

        var tool = new ToolDefinition
        {
            Name = "create_pull_request",
            ExecutionType = "api",
            Api = new ApiDefinition
            {
                Provider = "github",
                Endpoint = "/repos/{{owner}}/{{repo}}/pulls",
                Method = "POST",
                Headers = new()
                {
                    ["Authorization"] = "Bearer {{secrets.github_token}}",
                    ["Accept"] = "application/vnd.github+json"
                },
                Body = new()
                {
                    ["title"] = "{{title}}",
                    ["body"] = "{{description}}",
                    ["head"] = "{{branch}}",
                    ["base"] = "{{base_branch}}"
                }
            }
        };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = new()
            {
                ["owner"] = "owner",
                ["repo"] = "my-repo",
                ["title"] = "duplicate PR",
                ["description"] = "This should fail",
                ["branch"] = "feature/existing",
                ["base_branch"] = "main"
            }
        };

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(422, result.Output["status_code"]);
        Assert.Contains("Validation Failed", result.Output["body"]?.ToString() ?? "");
        Assert.NotEmpty(result.Errors);
        Assert.Contains("422", result.Errors[0]);
    }

    [Fact]
    public async Task CreatePullRequest_MockApi_SendsCorrectHeadersAndBody()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"number\": 1}",
                    System.Text.Encoding.UTF8, "application/json")
            });

        var executor = CreateExecutorWithHttpHandler(handler);

        var tool = new ToolDefinition
        {
            Name = "create_pull_request",
            ExecutionType = "api",
            Api = new ApiDefinition
            {
                Provider = "github",
                Endpoint = "/repos/{{owner}}/{{repo}}/pulls",
                Method = "POST",
                Headers = new()
                {
                    ["Authorization"] = "Bearer {{secrets.github_token}}",
                    ["Accept"] = "application/vnd.github+json"
                },
                Body = new()
                {
                    ["title"] = "{{title}}",
                    ["body"] = "{{description}}",
                    ["head"] = "{{branch}}",
                    ["base"] = "{{base_branch}}"
                }
            }
        };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = new()
            {
                ["owner"] = "testowner",
                ["repo"] = "testrepo",
                ["title"] = "PR Title",
                ["description"] = "PR Body",
                ["branch"] = "feature/x",
                ["base_branch"] = "develop"
            }
        };

        await executor.ExecuteAsync(request, CancellationToken.None);

        var req = handler.LastRequest!;
        Assert.Equal("https://api.github.com/repos/testowner/testrepo/pulls", req.RequestUri!.ToString());
        Assert.True(req.Headers.Contains("Accept"));

        var bodyStr = handler.LastRequestBody!;
        Assert.Contains("PR Title", bodyStr);
        Assert.Contains("PR Body", bodyStr);
        Assert.Contains("feature/x", bodyStr);
        Assert.Contains("develop", bodyStr);
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

    private Task<ToolResult> ExecuteGitCommand(string toolName, string commandTemplate,
        Dictionary<string, string> inputs)
    {
        var tool = new ToolDefinition
        {
            Name = toolName,
            ExecutionType = "command",
            Command = commandTemplate
        };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = inputs,
            WorkingDirectory = _tempWorkspace
        };

        return _executor.ExecuteAsync(request, CancellationToken.None);
    }

    private Task<ToolResult> ExecuteBuildCommand(string toolName, string category, string commandOverride)
    {
        var tool = new ToolDefinition
        {
            Name = toolName,
            Category = category,
            ExecutionType = "command",
            Command = commandOverride
        };

        var request = new ToolExecutionRequest
        {
            Tool = tool,
            Inputs = new(),
            WorkingDirectory = _tempWorkspace
        };

        return _executor.ExecuteAsync(request, CancellationToken.None);
    }

    private static async Task RunGit(string args, string workDir)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"git {args} failed (exit {process.ExitCode}): {stderr}");
        }
    }

    private ToolExecutor CreateExecutorWithHttpHandler(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workspace:BasePath"] = _tempWorkspace,
                ["GitHub:Token"] = "fake-token",
                ["GitHub:ApiUrl"] = "https://api.github.com"
            })
            .Build();

        return new ToolExecutor(
            new MockHttpClientFactory(handler),
            config,
            new NullLogger<ToolExecutor>());
    }

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(ct);
            return _handler(request);
        }
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public MockHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
