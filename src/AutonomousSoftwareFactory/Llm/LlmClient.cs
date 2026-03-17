using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutonomousSoftwareFactory.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AutonomousSoftwareFactory.Llm;

public class LlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LlmClient> _logger;
    private readonly IRunLogger _runLogger;
    private readonly string _model;

    public LlmClient(HttpClient httpClient, IConfiguration configuration, ILogger<LlmClient> logger, IRunLogger? runLogger = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _runLogger = runLogger ?? NullRunLogger.Instance;

        var provider = configuration["Llm:Provider"] ?? "OpenAI";
        _model = configuration["Llm:Model"] ?? "gpt-4.1";
        var apiKey = configuration["Llm:ApiKey"] ?? string.Empty;

        _httpClient.BaseAddress ??= provider switch
        {
            "OpenAI" => new Uri("https://api.openai.com/"),
            _ => new Uri("https://api.openai.com/")
        };

        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        var promptSummary = prompt.Length > 120 ? prompt[..120] + "..." : prompt;
        _logger.LogInformation("LLM call starting — model={Model}, prompt={Prompt}", _model, promptSummary);

        var startTime = DateTime.UtcNow;

        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("v1/chat/completions", content, ct);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("LLM call cancelled");
            throw;
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("LLM call timed out");
            throw new HttpRequestException("LLM request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "LLM call failed with network error");
            throw;
        }

        var duration = DateTime.UtcNow - startTime;

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("LLM call failed — status={Status}, body={Body}, duration={Duration}ms",
                response.StatusCode, errorBody, duration.TotalMilliseconds);
            throw new HttpRequestException($"LLM API returned {response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var result = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        var tokensUsed = root.TryGetProperty("usage", out var usage)
            ? usage.GetProperty("total_tokens").GetInt32()
            : -1;

        var resultSummary = result.Length > 120 ? result[..120] + "..." : result;
        _logger.LogInformation(
            "LLM call completed — tokens={Tokens}, duration={Duration}ms, response={Response}",
            tokensUsed, duration.TotalMilliseconds, resultSummary);

        _runLogger.LogLlmCall(_model, promptSummary, resultSummary, tokensUsed, duration.TotalMilliseconds);

        return result;
    }
}
