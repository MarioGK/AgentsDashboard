using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class ImagesApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Test]
    public async Task ListImages_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/images");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ListImages_WithFilter_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/images?filter=test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task BuildImage_ReturnsValidationProblem_WhenDockerfileEmpty()
    {
        var request = new BuildImageRequest("", "test-image:latest");
        var response = await _client.PostAsJsonAsync("/api/images/build", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BuildImage_ReturnsValidationProblem_WhenTagEmpty()
    {
        var request = new BuildImageRequest("FROM alpine", "");
        var response = await _client.PostAsJsonAsync("/api/images/build", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DeleteImage_ReturnsBadRequest_WhenImageDoesNotExist()
    {
        var response = await _client.DeleteAsync("/api/images/nonexistent:latest");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
