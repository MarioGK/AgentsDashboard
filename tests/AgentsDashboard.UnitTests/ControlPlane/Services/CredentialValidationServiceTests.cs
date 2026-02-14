using System.Net;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class CredentialValidationServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<CredentialValidationService>> _loggerMock;
    private readonly CredentialValidationService _service;

    public CredentialValidationServiceTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<CredentialValidationService>>();
        _service = new CredentialValidationService(_httpClientFactoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task ValidateAsync_WithUnknownProvider_ReturnsFalse()
    {
        var result = await _service.ValidateAsync("unknown-provider", "some-key", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Unknown provider");
    }

    [Theory]
    [InlineData("GITHUB")]
    [InlineData("github")]
    [InlineData("GitHub")]
    public async Task ValidateAsync_GitHubProvider_CallsCorrectEndpoint(string provider)
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, """{"login":"testuser"}""");
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var result = await _service.ValidateAsync(provider, "gh-token", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Authenticated as testuser");
        handler.RequestUri?.ToString().Should().Contain("api.github.com/user");
    }

    [Fact]
    public async Task ValidateAsync_GitHubUnauthorized_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Unauthorized, "");
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var result = await _service.ValidateAsync("github", "bad-token", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("401");
    }

    [Theory]
    [InlineData("claude-code")]
    [InlineData("CLAUDE-CODE")]
    public async Task ValidateAsync_ClaudeCodeProvider_CallsAnthropicApi(string provider)
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, """{"content":[]}""");
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var result = await _service.ValidateAsync(provider, "sk-ant-test", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Anthropic API key is valid");
        handler.RequestUri?.ToString().Should().Contain("api.anthropic.com");
    }

    [Fact]
    public async Task ValidateAsync_ClaudeCodeRateLimited_ReturnsTrue()
    {
        var handler = new MockHttpMessageHandler((HttpStatusCode)429, "");
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var result = await _service.ValidateAsync("claude-code", "sk-ant-test", CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ClaudeCodeUnauthorized_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Unauthorized, "");
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var result = await _service.ValidateAsync("claude-code", "bad-key", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid Anthropic API key");
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("opencode")]
    public async Task ValidateAsync_OpenAiProvider_CallsOpenAiApi(string provider)
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, """{"data":[]}""");
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var result = await _service.ValidateAsync(provider, "sk-test", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("OpenAI API key is valid");
        handler.RequestUri?.ToString().Should().Contain("api.openai.com/v1/models");
    }

    [Fact]
    public async Task ValidateAsync_OpenAiUnauthorized_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Unauthorized, "");
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var result = await _service.ValidateAsync("codex", "bad-key", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid OpenAI API key");
    }

    [Fact]
    public async Task ValidateAsync_ZaiProvider_CallsZaiApi()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, """{"data":[]}""");
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var result = await _service.ValidateAsync("zai", "zai-api-key", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Z.ai API key is valid");
        handler.RequestUri?.ToString().Should().Contain("open.bigmodel.cn");
    }

    [Fact]
    public async Task ValidateAsync_ZaiUnauthorized_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Unauthorized, "");
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var result = await _service.ValidateAsync("zai", "bad-key", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid Z.ai API key");
    }

    [Fact]
    public async Task ValidateAsync_NetworkError_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(new HttpRequestException("Network error"));
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var result = await _service.ValidateAsync("github", "token", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Connection failed");
    }
}

file class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _content;
    private readonly Exception? _exception;
    public Uri? RequestUri { get; private set; }

    public MockHttpMessageHandler(HttpStatusCode statusCode, string content)
    {
        _statusCode = statusCode;
        _content = content;
    }

    public MockHttpMessageHandler(Exception exception)
    {
        _exception = exception;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUri = request.RequestUri;

        if (_exception != null)
            throw _exception;

        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
