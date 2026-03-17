namespace AutonomousSoftwareFactory.Tests;

using AutonomousSoftwareFactory.Logging;

public class FileRunLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public FileRunLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "asf-test-logs-" + Guid.NewGuid().ToString("N")[..8]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static string ReadLogFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    private static string[] ReadLogLines(string path)
    {
        return ReadLogFile(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Constructor_CreatesLogFile_WithTimestampAndWorkflowName()
    {
        using var logger = new FileRunLogger(_tempDir, "my-workflow");

        Assert.True(File.Exists(logger.FilePath));
        Assert.Contains("my-workflow", Path.GetFileName(logger.FilePath));
        Assert.EndsWith(".log", logger.FilePath);
    }

    [Fact]
    public void Constructor_CreatesLogDirectory_WhenItDoesNotExist()
    {
        var nested = Path.Combine(_tempDir, "sub", "dir");
        using var logger = new FileRunLogger(nested, "test");

        Assert.True(Directory.Exists(nested));
    }

    [Fact]
    public void Constructor_SanitizesWorkflowName_InFileName()
    {
        using var logger = new FileRunLogger(_tempDir, "workflow with spaces/and:slashes");

        var fileName = Path.GetFileName(logger.FilePath);
        Assert.DoesNotContain(" ", fileName);
        Assert.DoesNotContain("/", fileName);
        Assert.DoesNotContain(":", fileName);
    }

    [Fact]
    public void LogWorkflowStart_WritesStructuredEntry()
    {
        using var logger = new FileRunLogger(_tempDir, "test-wf");

        logger.LogWorkflowStart("test-wf", 5);

        var content = ReadLogFile(logger.FilePath);
        Assert.Contains("[WORKFLOW_START]", content);
        Assert.Contains("\"workflow\":\"test-wf\"", content);
        Assert.Contains("\"steps\":5", content);
    }

    [Fact]
    public void LogWorkflowEnd_WritesStatusAndDuration()
    {
        using var logger = new FileRunLogger(_tempDir, "test-wf");

        logger.LogWorkflowEnd("test-wf", "completed", TimeSpan.FromSeconds(3.5));

        var content = ReadLogFile(logger.FilePath);
        Assert.Contains("[WORKFLOW_END]", content);
        Assert.Contains("\"status\":\"completed\"", content);
        Assert.Contains("\"duration_ms\":", content);
    }

    [Fact]
    public void LogStepStart_WritesStepDetails()
    {
        using var logger = new FileRunLogger(_tempDir, "test-wf");

        logger.LogStepStart("step_1", "First Step", "agent", 1, 3);

        var content = ReadLogFile(logger.FilePath);
        Assert.Contains("[STEP_START]", content);
        Assert.Contains("\"step_id\":\"step_1\"", content);
        Assert.Contains("\"step_name\":\"First Step\"", content);
        Assert.Contains("\"type\":\"agent\"", content);
        Assert.Contains("\"attempt\":1", content);
        Assert.Contains("\"max_attempts\":3", content);
    }

    [Fact]
    public void LogStepEnd_WritesStatusAndDuration()
    {
        using var logger = new FileRunLogger(_tempDir, "test-wf");

        logger.LogStepEnd("step_1", "success", TimeSpan.FromMilliseconds(150));

        var content = ReadLogFile(logger.FilePath);
        Assert.Contains("[STEP_END]", content);
        Assert.Contains("\"step_id\":\"step_1\"", content);
        Assert.Contains("\"status\":\"success\"", content);
    }

    [Fact]
    public void LogStepEnd_WithErrors_WritesErrorsList()
    {
        using var logger = new FileRunLogger(_tempDir, "test-wf");

        logger.LogStepEnd("step_1", "failed", TimeSpan.FromMilliseconds(200),
            ["Connection refused", "Timeout exceeded"]);

        var content = ReadLogFile(logger.FilePath);
        Assert.Contains("[STEP_END]", content);
        Assert.Contains("\"status\":\"failed\"", content);
        Assert.Contains("Connection refused", content);
        Assert.Contains("Timeout exceeded", content);
    }

    [Fact]
    public void LogLlmCall_WritesPromptAndResponseSummary()
    {
        using var logger = new FileRunLogger(_tempDir, "test-wf");

        logger.LogLlmCall("gpt-4", "Analyze the requirement...", "The analysis result...", 1500, 2345.6);

        var content = ReadLogFile(logger.FilePath);
        Assert.Contains("[LLM_CALL]", content);
        Assert.Contains("\"model\":\"gpt-4\"", content);
        Assert.Contains("Analyze the requirement...", content);
        Assert.Contains("The analysis result...", content);
        Assert.Contains("\"tokens\":1500", content);
    }

    [Fact]
    public void LogToolExecution_WritesToolDetails()
    {
        using var logger = new FileRunLogger(_tempDir, "test-wf");

        logger.LogToolExecution("dotnet_build", "command", "dotnet build", true, "Build succeeded.", null);

        var content = ReadLogFile(logger.FilePath);
        Assert.Contains("[TOOL_EXEC]", content);
        Assert.Contains("\"tool\":\"dotnet_build\"", content);
        Assert.Contains("\"execution_type\":\"command\"", content);
        Assert.Contains("\"command\":\"dotnet build\"", content);
        Assert.Contains("\"success\":true", content);
        Assert.Contains("Build succeeded.", content);
    }

    [Fact]
    public void LogToolExecution_WithErrors_WritesErrorsList()
    {
        using var logger = new FileRunLogger(_tempDir, "test-wf");

        logger.LogToolExecution("git_push", "command", "git push", false, null,
            ["remote rejected", "permission denied"]);

        var content = ReadLogFile(logger.FilePath);
        Assert.Contains("[TOOL_EXEC]", content);
        Assert.Contains("\"success\":false", content);
        Assert.Contains("remote rejected", content);
        Assert.Contains("permission denied", content);
    }

    [Fact]
    public void LogToolExecution_TruncatesLongOutput()
    {
        using var logger = new FileRunLogger(_tempDir, "test-wf");

        var longOutput = new string('x', 1000);
        logger.LogToolExecution("read_files", "internal", null, true, longOutput, null);

        var content = ReadLogFile(logger.FilePath);
        Assert.Contains("...", content);
        // Should be truncated at 500 chars + "..."
        Assert.DoesNotContain(longOutput, content);
    }

    [Fact]
    public void MultipleLogEntries_WriteSequentially_WithTimestamps()
    {
        using var logger = new FileRunLogger(_tempDir, "test-wf");

        logger.LogWorkflowStart("test-wf", 3);
        logger.LogStepStart("s1", "Step 1", "input", 1, 1);
        logger.LogStepEnd("s1", "success", TimeSpan.FromMilliseconds(10));
        logger.LogWorkflowEnd("test-wf", "completed", TimeSpan.FromSeconds(1));

        var lines = ReadLogLines(logger.FilePath);
        Assert.Equal(4, lines.Length);
        Assert.Contains("[WORKFLOW_START]", lines[0]);
        Assert.Contains("[STEP_START]", lines[1]);
        Assert.Contains("[STEP_END]", lines[2]);
        Assert.Contains("[WORKFLOW_END]", lines[3]);
    }

    [Fact]
    public void NullRunLogger_DoesNotThrow()
    {
        var logger = NullRunLogger.Instance;

        logger.LogWorkflowStart("wf", 1);
        logger.LogWorkflowEnd("wf", "completed", TimeSpan.Zero);
        logger.LogStepStart("s1", "Step", "input", 1, 1);
        logger.LogStepEnd("s1", "success", TimeSpan.Zero);
        logger.LogLlmCall("model", "prompt", "response", 0, 0);
        logger.LogToolExecution("tool", "internal", null, true, null, null);
    }
}
