using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace OpenApiContractValidation.Tests.E2E;

/// <summary>
/// Happy-path end-to-end tests driving the SampleApi through real HTTP via
/// <see cref="WebApplicationFactory{Program}"/>. These prove that fully-conformant requests and
/// responses pass the middleware cleanly and reach the client intact.
/// </summary>
public class HappyPathE2ETests
{
    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>();

    [Fact]
    public async Task GetUserById_ReturnsValidUser()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/users/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await ParseJsonAsync(response);
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Alice", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetUserById_RecursiveManagerIsValid()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/users/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await ParseJsonAsync(response);
        var manager = doc.RootElement.GetProperty("manager");
        Assert.Equal(2, manager.GetProperty("id").GetInt32());
        Assert.Equal("Bob", manager.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetUsersMe_LiteralBeatsTemplatedPath()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await ParseJsonAsync(response);
        Assert.Equal(99, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Me", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task PostUser_Returns201WithLocationAndValidBody()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var content = new StringContent(
            """{"name":"y","email":"y@x.com"}""",
            Encoding.UTF8,
            "application/json"
        );
        using var response = await client.PostAsync("/users", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/users/2", response.Headers.Location?.OriginalString);
        var doc = await ParseJsonAsync(response);
        Assert.Equal(2, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("y", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ListUsers_WithRichParameterStyles_Passes()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/users?status=active&ids=1,2,3&tags=a%20b&codes=x%7Cy"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await ParseJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListUsers_DeepObjectFilter_Passes()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/users?filter%5Bregion%5D=eu");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteNoContent_Returns204WithNoBody()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/no-content");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact]
    public async Task GetEtag_Returns304WithNoBody()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/etag");

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    private static async Task<JsonDocument> ParseJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }
}
