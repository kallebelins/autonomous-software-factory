namespace AutonomousSoftwareFactory.Tools;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutonomousSoftwareFactory.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public partial class ToolExecutor : IToolExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ToolExecutor> _logger;
    private readonly string _workspaceBasePath;

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".git", ".vs", ".idea"
    };

    public ToolExecutor(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ToolExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _workspaceBasePath = Path.GetFullPath(
            configuration["Workspace:BasePath"] ?? "./workspace");
    }

    public async Task<ToolResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        var tool = request.Tool;

        _logger.LogInformation("ToolExecutor: executing '{Tool}' (type={Type})",
            tool.Name, tool.ExecutionType);

        try
        {
            return tool.ExecutionType switch
            {
                "command" => await ExecuteCommandAsync(tool, request.Inputs, request.WorkingDirectory, ct),
                "api" => await ExecuteApiAsync(tool, request.Inputs, ct),
                "internal" => await ExecuteInternalAsync(tool, request.Inputs),
                _ => new ToolResult
                {
                    Success = false,
                    Errors = [$"Unknown execution_type '{tool.ExecutionType}'"]
                }
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ToolExecutor: unhandled error on tool '{Tool}'", tool.Name);
            return new ToolResult
            {
                Success = false,
                Errors = [ex.Message]
            };
        }
    }

    // ───────────────────────────── command ─────────────────────────────

    private async Task<ToolResult> ExecuteCommandAsync(
        ToolDefinition tool, Dictionary<string, string> inputs, string workingDirectory, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tool.Command))
            return new ToolResult { Success = false, Errors = ["Tool has no command template"] };

        // Git-specific pre-validation and input enrichment
        var preCheck = ValidateGitInputs(tool.Name, inputs);
        if (preCheck is not null) return preCheck;

        // Platform-specific command template adjustment (single quotes don't work in cmd.exe)
        var commandTemplate = tool.Command;
        if (OperatingSystem.IsWindows() && tool.Name == "git_commit")
            commandTemplate = commandTemplate.Replace("'{{message}}'", "\"{{message}}\"");

        var command = ReplacePlaceholders(commandTemplate, inputs);
        var workDir = ResolveWorkingDirectory(inputs, workingDirectory);

        _logger.LogInformation("ToolExecutor: running command '{Command}' in '{Dir}'",
            MaskSecretsInCommand(command), workDir);

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/sh",
            Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        var exitCode = process.ExitCode;

        _logger.LogInformation("ToolExecutor: command exited with code {Code}", exitCode);

        var result = new ToolResult
        {
            Success = exitCode == 0,
            Output = new Dictionary<string, object>
            {
                ["stdout"] = stdout.ToString().TrimEnd(),
                ["stderr"] = stderr.ToString().TrimEnd(),
                ["exit_code"] = exitCode
            }
        };

        if (exitCode != 0)
            result.Errors.Add($"Command exited with code {exitCode}: {stderr.ToString().TrimEnd()}");

        // Git-specific output enrichment
        if (tool.Name == "git_clone" && result.Success)
            result.Output["local_path"] = inputs.GetValueOrDefault("destination", "").Trim('"');

        // Build/test/lint output enrichment
        EnrichCommandOutput(tool, result);

        return result;
    }

    // ───────────────────────────── output enrichment ───────────────────

    private static void EnrichCommandOutput(ToolDefinition tool, ToolResult result)
    {
        var stdoutStr = result.Output.TryGetValue("stdout", out var stdout) ? stdout?.ToString() ?? "" : "";
        var stderrStr = result.Output.TryGetValue("stderr", out var stderr) ? stderr?.ToString() ?? "" : "";
        var combined = stdoutStr + "\n" + stderrStr;

        switch (tool.Category)
        {
            case "test":
                EnrichTestOutput(tool.Name, combined, result);
                break;
            case "quality":
                EnrichQualityOutput(tool.Name, combined, result);
                break;
        }
    }

    private static void EnrichTestOutput(string toolName, string output, ToolResult result)
    {
        switch (toolName)
        {
            case "dotnet_test":
                ParseDotnetTestOutput(output, result);
                break;
            case "junit_test":
                ParseMavenTestOutput(output, result);
                break;
            case "jest_test":
                ParseJestTestOutput(output, result);
                break;
        }
    }

    private static void ParseDotnetTestOutput(string output, ToolResult result)
    {
        // Pattern: "Total: 10, Passed: 8, Failed: 2"  or  "Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5"
        var match = DotnetTestTotalRegex().Match(output);
        if (match.Success)
        {
            if (int.TryParse(match.Groups["total"].Value, out var total))
                result.Output["total"] = total;
            if (int.TryParse(match.Groups["passed"].Value, out var passed))
                result.Output["passed"] = passed;
            if (int.TryParse(match.Groups["failed"].Value, out var failed))
                result.Output["failed"] = failed;
        }
    }

    private static void ParseMavenTestOutput(string output, ToolResult result)
    {
        // Pattern: "Tests run: 10, Failures: 2, Errors: 1, Skipped: 0"
        var match = MavenTestRegex().Match(output);
        if (match.Success)
        {
            if (int.TryParse(match.Groups["run"].Value, out var run))
                result.Output["total"] = run;
            if (int.TryParse(match.Groups["failures"].Value, out var failures)
                && int.TryParse(match.Groups["errors"].Value, out var errors))
            {
                result.Output["failed"] = failures + errors;
                result.Output["passed"] = run - failures - errors;
            }
        }
    }

    private static void ParseJestTestOutput(string output, ToolResult result)
    {
        // Pattern: "Tests:       2 passed, 2 total"  or  "Tests:       1 failed, 3 passed, 4 total"
        var match = JestTestTotalRegex().Match(output);
        if (match.Success)
        {
            if (int.TryParse(match.Groups["total"].Value, out var total))
                result.Output["total"] = total;
        }

        var passedMatch = JestTestPassedRegex().Match(output);
        if (passedMatch.Success && int.TryParse(passedMatch.Groups["passed"].Value, out var passed))
            result.Output["passed"] = passed;

        var failedMatch = JestTestFailedRegex().Match(output);
        if (failedMatch.Success && int.TryParse(failedMatch.Groups["failed"].Value, out var failed))
            result.Output["failed"] = failed;
        else
            result.Output["failed"] = 0;
    }

    private static void EnrichQualityOutput(string toolName, string output, ToolResult result)
    {
        switch (toolName)
        {
            case "dotnet_format":
                ParseDotnetFormatOutput(output, result);
                break;
            case "eslint":
                ParseEslintOutput(output, result);
                break;
            case "checkstyle":
                ParseCheckstyleOutput(output, result);
                break;
        }
    }

    private static void ParseDotnetFormatOutput(string output, ToolResult result)
    {
        // dotnet format --verify-no-changes exits non-zero when there are formatting issues
        // Count lines that contain file paths with formatting changes
        var issues = DotnetFormatIssueRegex().Matches(output);
        result.Output["issues"] = issues.Select(m => m.Value.Trim()).ToList();
    }

    private static void ParseEslintOutput(string output, ToolResult result)
    {
        // Count problem lines: "X problems (Y errors, Z warnings)" or individual file issues
        var match = EslintProblemsRegex().Match(output);
        if (match.Success)
        {
            result.Output["issues"] = new List<string> { match.Value.Trim() };
        }
        else
        {
            // Collect lines that look like ESLint warnings/errors
            var issues = output.Split('\n')
                .Where(l => EslintLineIssueRegex().IsMatch(l))
                .Select(l => l.Trim())
                .ToList();
            result.Output["issues"] = issues;
        }
    }

    private static void ParseCheckstyleOutput(string output, ToolResult result)
    {
        // Maven checkstyle: "[ERROR]" lines indicate violations
        var issues = output.Split('\n')
            .Where(l => l.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase)
                        || l.Contains("[WARNING]", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Trim())
            .ToList();
        result.Output["issues"] = issues;
    }

    // ───────────────────────────── regex patterns (compiled) ───────────

    [GeneratedRegex(@"Failed:\s*(?<failed>\d+).*Passed:\s*(?<passed>\d+).*Total:\s*(?<total>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DotnetTestTotalRegex();

    [GeneratedRegex(@"Tests run:\s*(?<run>\d+),\s*Failures:\s*(?<failures>\d+),\s*Errors:\s*(?<errors>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MavenTestRegex();

    [GeneratedRegex(@"Tests:\s+.*?(?<total>\d+)\s+total", RegexOptions.IgnoreCase)]
    private static partial Regex JestTestTotalRegex();

    [GeneratedRegex(@"(?<passed>\d+)\s+passed", RegexOptions.IgnoreCase)]
    private static partial Regex JestTestPassedRegex();

    [GeneratedRegex(@"(?<failed>\d+)\s+failed", RegexOptions.IgnoreCase)]
    private static partial Regex JestTestFailedRegex();

    [GeneratedRegex(@"^\s*\S+.*\(\d+,\d+\):", RegexOptions.Multiline)]
    private static partial Regex DotnetFormatIssueRegex();

    [GeneratedRegex(@"\d+\s+problems?\s*\(\d+\s+errors?,\s*\d+\s+warnings?\)", RegexOptions.IgnoreCase)]
    private static partial Regex EslintProblemsRegex();

    [GeneratedRegex(@"\d+:\d+\s+(error|warning)\s+")]
    private static partial Regex EslintLineIssueRegex();

    // ───────────────────────────── api ─────────────────────────────────

    private async Task<ToolResult> ExecuteApiAsync(
        ToolDefinition tool, Dictionary<string, string> inputs, CancellationToken ct)
    {
        if (tool.Api is null)
            return new ToolResult { Success = false, Errors = ["Tool has no API definition"] };

        // Validate GitHub token for PR creation
        if (tool.Name == "create_pull_request")
        {
            var token = _configuration["GitHub:Token"];
            if (string.IsNullOrEmpty(token))
                return new ToolResult
                {
                    Success = false,
                    Errors = ["GitHub token is not configured. Set 'GitHub:Token' in appsettings.json."]
                };
        }

        var api = tool.Api;
        var allPlaceholders = new Dictionary<string, string>(inputs);

        // Inject secrets (e.g. github_token)
        var githubToken = _configuration["GitHub:Token"];
        if (!string.IsNullOrEmpty(githubToken))
            allPlaceholders["secrets.github_token"] = githubToken;

        var baseUrl = api.Provider.Equals("github", StringComparison.OrdinalIgnoreCase)
            ? _configuration["GitHub:ApiUrl"] ?? "https://api.github.com"
            : "";

        var endpoint = ReplacePlaceholders(api.Endpoint, allPlaceholders);
        var url = baseUrl + endpoint;
        var method = new HttpMethod(api.Method.ToUpperInvariant());

        _logger.LogInformation("ToolExecutor: API {Method} {Url}", method, url);

        using var request = new HttpRequestMessage(method, url);

        foreach (var (key, value) in api.Headers)
            request.Headers.TryAddWithoutValidation(key, ReplacePlaceholders(value, allPlaceholders));

        if (api.Body.Count > 0 && method != HttpMethod.Get)
        {
            var bodyDict = new Dictionary<string, string>();
            foreach (var (key, value) in api.Body)
                bodyDict[key] = ReplacePlaceholders(value, allPlaceholders);

            request.Content = new StringContent(
                JsonSerializer.Serialize(bodyDict),
                Encoding.UTF8,
                "application/json");
        }

        var client = _httpClientFactory.CreateClient();
        var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        _logger.LogInformation("ToolExecutor: API responded {StatusCode}", (int)response.StatusCode);

        var result = new ToolResult
        {
            Success = response.IsSuccessStatusCode,
            Output = new Dictionary<string, object>
            {
                ["status_code"] = (int)response.StatusCode,
                ["body"] = responseBody
            }
        };

        if (!response.IsSuccessStatusCode)
            result.Errors.Add($"API returned {(int)response.StatusCode}: {responseBody}");

        return result;
    }

    // ───────────────────────────── internal ────────────────────────────

    private Task<ToolResult> ExecuteInternalAsync(ToolDefinition tool, Dictionary<string, string> inputs)
    {
        return Task.FromResult(tool.Name switch
        {
            "read_files" => ReadFiles(inputs),
            "list_directory" => ListDirectory(inputs),
            "search_files" => SearchFiles(inputs),
            "write_file" => WriteFile(inputs),
            "create_file" => CreateFile(inputs),
            "delete_file" => DeleteFile(inputs),
            _ => new ToolResult { Success = false, Errors = [$"Unknown internal tool '{tool.Name}'"] }
        });
    }

    private ToolResult ReadFiles(Dictionary<string, string> inputs)
    {
        if (!inputs.TryGetValue("path", out var path))
            return new ToolResult { Success = false, Errors = ["Missing required input 'path'"] };

        var fullPath = ResolveSafePath(path);
        if (fullPath is null)
            return new ToolResult { Success = false, Errors = ["Path is outside the workspace"] };

        if (!File.Exists(fullPath))
            return new ToolResult { Success = false, Errors = [$"File not found: {path}"] };

        var content = File.ReadAllText(fullPath);
        return new ToolResult
        {
            Success = true,
            Output = new Dictionary<string, object> { ["content"] = content }
        };
    }

    private ToolResult ListDirectory(Dictionary<string, string> inputs)
    {
        if (!inputs.TryGetValue("path", out var path))
            return new ToolResult { Success = false, Errors = ["Missing required input 'path'"] };

        var fullPath = ResolveSafePath(path);
        if (fullPath is null)
            return new ToolResult { Success = false, Errors = ["Path is outside the workspace"] };

        if (!Directory.Exists(fullPath))
            return new ToolResult { Success = false, Errors = [$"Directory not found: {path}"] };

        var entries = Directory.GetFileSystemEntries(fullPath)
            .Select(e => Path.GetRelativePath(fullPath, e))
            .ToList();

        return new ToolResult
        {
            Success = true,
            Output = new Dictionary<string, object> { ["files"] = entries }
        };
    }

    private ToolResult SearchFiles(Dictionary<string, string> inputs)
    {
        if (!inputs.TryGetValue("pattern", out var pattern))
            return new ToolResult { Success = false, Errors = ["Missing required input 'pattern'"] };

        inputs.TryGetValue("path", out var basePath);
        inputs.TryGetValue("file_filter", out var fileFilter);

        var searchDir = basePath is not null ? ResolveSafePath(basePath) : _workspaceBasePath;
        if (searchDir is null)
            return new ToolResult { Success = false, Errors = ["Path is outside the workspace"] };

        if (!Directory.Exists(searchDir))
            return new ToolResult { Success = false, Errors = [$"Directory not found: {basePath}"] };

        var matches = new List<object>();
        var searchPattern = string.IsNullOrWhiteSpace(fileFilter) ? "*" : fileFilter;

        SearchDirectory(searchDir, searchPattern, pattern, matches, maxDepth: 10, currentDepth: 0);

        return new ToolResult
        {
            Success = true,
            Output = new Dictionary<string, object> { ["matches"] = matches }
        };
    }

    private void SearchDirectory(
        string dir, string searchPattern, string textPattern,
        List<object> matches, int maxDepth, int currentDepth)
    {
        if (currentDepth > maxDepth) return;

        try
        {
            foreach (var file in Directory.GetFiles(dir, searchPattern))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(textPattern, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(new
                        {
                            file = Path.GetRelativePath(_workspaceBasePath, file),
                            line = i + 1,
                            text = lines[i].Trim()
                        });
                    }
                }
            }

            foreach (var subDir in Directory.GetDirectories(dir))
            {
                var dirName = Path.GetFileName(subDir);
                if (!IgnoredDirectories.Contains(dirName))
                    SearchDirectory(subDir, searchPattern, textPattern, matches, maxDepth, currentDepth + 1);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we don't have permission to access
        }
    }

    private ToolResult WriteFile(Dictionary<string, string> inputs)
    {
        if (!inputs.TryGetValue("path", out var path))
            return new ToolResult { Success = false, Errors = ["Missing required input 'path'"] };
        if (!inputs.TryGetValue("content", out var content))
            return new ToolResult { Success = false, Errors = ["Missing required input 'content'"] };

        var fullPath = ResolveSafePath(path);
        if (fullPath is null)
            return new ToolResult { Success = false, Errors = ["Path is outside the workspace"] };

        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content);
        _logger.LogInformation("ToolExecutor: wrote file '{Path}'", path);

        return new ToolResult
        {
            Success = true,
            Output = new Dictionary<string, object> { ["success"] = true }
        };
    }

    private ToolResult CreateFile(Dictionary<string, string> inputs)
    {
        if (!inputs.TryGetValue("path", out var path))
            return new ToolResult { Success = false, Errors = ["Missing required input 'path'"] };
        if (!inputs.TryGetValue("content", out var content))
            return new ToolResult { Success = false, Errors = ["Missing required input 'content'"] };

        var fullPath = ResolveSafePath(path);
        if (fullPath is null)
            return new ToolResult { Success = false, Errors = ["Path is outside the workspace"] };

        if (File.Exists(fullPath))
            return new ToolResult { Success = false, Errors = [$"File already exists: {path}"] };

        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content);
        _logger.LogInformation("ToolExecutor: created file '{Path}'", path);

        return new ToolResult
        {
            Success = true,
            Output = new Dictionary<string, object> { ["success"] = true }
        };
    }

    private ToolResult DeleteFile(Dictionary<string, string> inputs)
    {
        if (!inputs.TryGetValue("path", out var path))
            return new ToolResult { Success = false, Errors = ["Missing required input 'path'"] };

        var fullPath = ResolveSafePath(path);
        if (fullPath is null)
            return new ToolResult { Success = false, Errors = ["Path is outside the workspace"] };

        if (!File.Exists(fullPath))
            return new ToolResult { Success = false, Errors = [$"File not found: {path}"] };

        // Do not allow deleting directories
        if (Directory.Exists(fullPath))
            return new ToolResult { Success = false, Errors = ["Cannot delete a directory, only files"] };

        File.Delete(fullPath);
        _logger.LogInformation("ToolExecutor: deleted file '{Path}'", path);

        return new ToolResult
        {
            Success = true,
            Output = new Dictionary<string, object> { ["success"] = true }
        };
    }

    // ───────────────────────────── git operations ──────────────────────

    private ToolResult? ValidateGitInputs(string toolName, Dictionary<string, string> inputs)
    {
        return toolName switch
        {
            "git_clone" => ValidateAndEnrichGitClone(inputs),
            "git_commit" => ValidateGitCommit(inputs),
            _ => null
        };
    }

    private ToolResult? ValidateAndEnrichGitClone(Dictionary<string, string> inputs)
    {
        if (!inputs.TryGetValue("repo_url", out var repoUrl) || string.IsNullOrWhiteSpace(repoUrl))
            return new ToolResult { Success = false, Errors = ["Missing required input 'repo_url'"] };

        // Default destination to workspace/repos/{repo-name}
        if (!inputs.TryGetValue("destination", out var dest) || string.IsNullOrWhiteSpace(dest))
        {
            var repoName = ExtractRepoNameFromUrl(repoUrl);
            dest = Path.Combine(_workspaceBasePath, "repos", repoName);
            inputs["destination"] = dest;
        }
        else if (!Path.IsPathRooted(dest))
        {
            dest = Path.Combine(_workspaceBasePath, "repos", dest);
            inputs["destination"] = dest;
        }

        // Validate destination doesn't already exist
        if (Directory.Exists(dest))
            return new ToolResult { Success = false, Errors = [$"Destination directory already exists: {dest}"] };

        // Ensure parent directory exists
        var parentDir = Path.GetDirectoryName(dest);
        if (parentDir is not null && !Directory.Exists(parentDir))
            Directory.CreateDirectory(parentDir);

        // Inject GitHub token for HTTPS authentication
        var token = _configuration["GitHub:Token"];
        if (!string.IsNullOrEmpty(token) && repoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            inputs["repo_url"] = InjectTokenInGitUrl(repoUrl, token);

        // Default branch if not specified
        if (!inputs.ContainsKey("branch") || string.IsNullOrWhiteSpace(inputs["branch"]))
            inputs["branch"] = "main";

        // Quote paths containing spaces for command-line safety
        if (dest.Contains(' '))
            inputs["destination"] = $"\"{dest}\"";
        var currentUrl = inputs["repo_url"];
        if (currentUrl.Contains(' ') && !currentUrl.StartsWith('"'))
            inputs["repo_url"] = $"\"{currentUrl}\"";

        return null;
    }

    private static ToolResult? ValidateGitCommit(Dictionary<string, string> inputs)
    {
        if (!inputs.TryGetValue("message", out var message) || string.IsNullOrWhiteSpace(message))
            return new ToolResult { Success = false, Errors = ["Missing required input 'message'"] };

        return null;
    }

    internal static string ExtractRepoNameFromUrl(string repoUrl)
    {
        var name = repoUrl.TrimEnd('/');

        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        var lastSlash = name.LastIndexOf('/');
        if (lastSlash >= 0)
            name = name[(lastSlash + 1)..];

        // Handle SSH URLs (git@github.com:owner/repo)
        var lastColon = name.LastIndexOf(':');
        if (lastColon >= 0)
            name = name[(lastColon + 1)..];

        return string.IsNullOrEmpty(name) ? "repo" : name;
    }

    internal static string InjectTokenInGitUrl(string url, string token)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;

        var withoutScheme = url["https://".Length..];

        // Don't inject if credentials already present
        if (withoutScheme.Contains('@'))
            return url;

        return $"https://x-access-token:{token}@{withoutScheme}";
    }

    private string MaskSecretsInCommand(string command)
    {
        var token = _configuration["GitHub:Token"];
        if (!string.IsNullOrEmpty(token))
            return command.Replace(token, "***");
        return command;
    }

    // ───────────────────────────── stack detection ─────────────────────

    /// <summary>
    /// Detects the project stack based on files present in the given directory.
    /// Returns "dotnet", "maven", "npm" or "unknown".
    /// </summary>
    internal static string DetectStack(string directory)
    {
        if (Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0
            || Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly).Length > 0
            || Directory.GetFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly).Length > 0)
            return "dotnet";

        if (File.Exists(Path.Combine(directory, "pom.xml")))
            return "maven";

        if (File.Exists(Path.Combine(directory, "package.json")))
            return "npm";

        return "unknown";
    }

    // ───────────────────────────── helpers ─────────────────────────────

    /// <summary>
    /// Resolves a path relative to the workspace and verifies it stays inside it.
    /// Returns null if the path would escape the workspace.
    /// </summary>
    internal string? ResolveSafePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_workspaceBasePath, relativePath));
        return fullPath.StartsWith(_workspaceBasePath, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    private static string ReplacePlaceholders(string template, Dictionary<string, string> values)
    {
        var result = template;
        foreach (var (key, value) in values)
            result = result.Replace($"{{{{{key}}}}}", value);
        return result;
    }

    private string ResolveWorkingDirectory(Dictionary<string, string> inputs, string requestWorkingDirectory)
    {
        // Prefer explicit working_directory from inputs, then from request, then workspace base
        if (inputs.TryGetValue("working_directory", out var dir) && !string.IsNullOrWhiteSpace(dir))
        {
            var resolved = Path.GetFullPath(dir);
            if (Directory.Exists(resolved)) return resolved;
        }

        if (!string.IsNullOrWhiteSpace(requestWorkingDirectory))
        {
            var resolved = Path.GetFullPath(requestWorkingDirectory);
            if (Directory.Exists(resolved)) return resolved;
        }

        return _workspaceBasePath;
    }
}
