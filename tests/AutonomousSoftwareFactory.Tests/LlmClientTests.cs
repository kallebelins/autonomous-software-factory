namespace AutonomousSoftwareFactory.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using AutonomousSoftwareFactory.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

public class LlmClientTests
{
    private static IConfiguration BuildConfig(
        string? provider = "OpenAI",
        string? model = "gpt-4.1",
        string? apiKey = "sk-test-key")
    {
        var data = new Dictionary<string, string?>();
        if (provider is not null) data["Llm:Provider"] = provider;
        if (model is not null) data["Llm:Model"] = model;
        if (apiKey is not null) data["Llm:ApiKey"] = apiKey;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }

    private static (LlmClient client, FakeHandler handler) CreateClient(
        string responseContent = "Hello from LLM",
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? provider = "OpenAI",
        string? model = "gpt-4.1",
        string? apiKey = "sk-test-key")
    {
        var apiResponse = new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content = responseContent } }
            },
            usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 }
        };

        var handler = new FakeHandler(JsonSerializer.Serialize(apiResponse), statusCode);
        var httpClient = new HttpClient(handler);
        var config = BuildConfig(provider, model, apiKey);

        var client = new LlmClient(httpClient, config, new NullLogger<LlmClient>());
        return (client, handler);
    }

    // ───────────────────── Request construction ─────────────────────

    [Fact]
    public async Task CompleteAsync_SendsPostToCorrectEndpoint()
    {
        var (client, handler) = CreateClient();

        await client.CompleteAsync("test prompt", CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal("v1/chat/completions", handler.LastRequest.RequestUri!.AbsolutePath.TrimStart('/'));
    }

    [Fact]
    public async Task CompleteAsync_RequestBody_ContainsModelAndMessages()
    {
        var (client, handler) = CreateClient(model: "gpt-4o");

        await client.CompleteAsync("Analyze this code", CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody);
        var root = doc.RootElement;

        Assert.Equal("gpt-4o", root.GetProperty("model").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("Analyze this code", messages[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_SetsAuthorizationHeader()
    {
        var (client, handler) = CreateClient(apiKey: "sk-my-secret");

        await client.CompleteAsync("test", CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("sk-my-secret", handler.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task CompleteAsync_NoApiKey_NoAuthorizationHeader()
    {
        var (client, handler) = CreateClient(apiKey: "");

        await client.CompleteAsync("test", CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Null(handler.LastRequest.Headers.Authorization);
    }

    // ───────────────────── Response parsing ──────────────────────────

    [Fact]
    public async Task CompleteAsync_ReturnsContentFromChoice()
    {
        var (client, _) = CreateClient(responseContent: "Analysis: .NET 8 project");

        var result = await client.CompleteAsync("analyze", CancellationToken.None);

        Assert.Equal("Analysis: .NET 8 project", result);
    }

    [Fact]
    public async Task CompleteAsync_NoUsageProperty_StillReturnsContent()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content = "no usage field" } }
            }
        });

        var handler = new FakeHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var config = BuildConfig();
        var client = new LlmClient(httpClient, config, new NullLogger<LlmClient>());

        var result = await client.CompleteAsync("test", CancellationToken.None);

        Assert.Equal("no usage field", result);
    }

    // ───────────────────── Error handling ────────────────────────────

    [Fact]
    public async Task CompleteAsync_HttpError_ThrowsHttpRequestException()
    {
        var (client, _) = CreateClient(
            statusCode: HttpStatusCode.InternalServerError,
            responseContent: "ignored");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CompleteAsync("test", CancellationToken.None));

        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Unauthorized_ThrowsHttpRequestException()
    {
        var (client, _) = CreateClient(
            statusCode: HttpStatusCode.Unauthorized,
            responseContent: "ignored");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CompleteAsync("test", CancellationToken.None));

        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var (client, _) = CreateClient();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CompleteAsync("test", cts.Token));
    }

    [Fact]
    public async Task CompleteAsync_Timeout_ThrowsHttpRequestException()
    {
        var handler = new TimeoutHandler();
        var httpClient = new HttpClient(handler);
        var config = BuildConfig();
        var client = new LlmClient(httpClient, config, new NullLogger<LlmClient>());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CompleteAsync("test", CancellationToken.None));
    }

    // ───────────────────── Default configuration ────────────────────

    [Fact]
    public async Task CompleteAsync_DefaultModel_UsesGpt41()
    {
        var (client, handler) = CreateClient(model: null);

        await client.CompleteAsync("test", CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody);
        Assert.Equal("gpt-4.1", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task CompleteAsync_DefaultProvider_UsesOpenAIBaseAddress()
    {
        var (client, handler) = CreateClient(provider: null);

        await client.CompleteAsync("test", CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("api.openai.com", handler.LastRequest.RequestUri!.Host);
    }

    // ───────────────────── Test doubles ──────────────────────────────

    private class FakeHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public FakeHandler(string responseBody, HttpStatusCode statusCode)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastRequest = request;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            if (_statusCode != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent($"{{\"error\": \"status {(int)_statusCode}\"}}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Simulate a timeout by throwing TaskCanceledException without the token being cancelled
            throw new TaskCanceledException("The request timed out.", null, CancellationToken.None);
        }
    }
}
