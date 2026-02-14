using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class ImagesApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task ListImages_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/images");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListImages_WithFilter_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/images?filter=test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BuildImage_ReturnsValidationProblem_WhenDockerfileEmpty()
    {
        var request = new BuildImageRequest("", "test-image:latest");
        var response = await _client.PostAsJsonAsync("/api/images/build", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BuildImage_ReturnsValidationProblem_WhenTagEmpty()
    {
        var request = new BuildImageRequest("FROM alpine", "");
        var response = await _client.PostAsJsonAsync("/api/images/build", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteImage_ReturnsBadRequest_WhenImageDoesNotExist()
    {
        var response = await _client.DeleteAsync("/api/images/nonexistent:latest");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
