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

        var command = ReplacePlaceholders(tool.Command, inputs);
        var workDir = ResolveWorkingDirectory(inputs, workingDirectory);

        _logger.LogInformation("ToolExecutor: running command '{Command}' in '{Dir}'", command, workDir);

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

        return result;
    }

    // ───────────────────────────── api ─────────────────────────────────

    private async Task<ToolResult> ExecuteApiAsync(
        ToolDefinition tool, Dictionary<string, string> inputs, CancellationToken ct)
    {
        if (tool.Api is null)
            return new ToolResult { Success = false, Errors = ["Tool has no API definition"] };

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
