using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenApiContractValidation.Models;
using Xunit;

namespace OpenApiContractValidation.Tests.E2E;

/// <summary>
/// End-to-end validation against the official Swagger Petstore (OpenAPI 3.0.4) contract,
/// exercising real request/response conformance for a production-grade spec: path templates,
/// path/query parameters, request and response bodies (including <c>$ref</c> composition),
/// status codes, content-type matching, and strict rejection of contract drift.
/// </summary>
public sealed class PetstoreE2ETests
{
    private const string ValidPetJson = """
        {
          "id": 10,
          "name": "doggie",
          "category": { "id": 1, "name": "Dogs" },
          "photoUrls": ["http://example.com/1.png"],
          "tags": [{ "id": 1, "name": "tag1" }],
          "status": "available"
        }
        """;

    private static RequestDelegate Json(int status, string body) =>
        async ctx =>
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(body);
        };

    [Fact]
    public async Task GetPetById_ConformantResponse_Passes()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidPetJson),
            new HttpRequestMessage(HttpMethod.Get, "/pet/10")
        );

        Assert.Null(outcome.Exception);
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
        Assert.Contains("doggie", await outcome.Response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FindByStatus_ValidEnumQueryAndArrayBody_Passes()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, $"[{ValidPetJson}]"),
            new HttpRequestMessage(HttpMethod.Get, "/pet/findByStatus?status=available")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task PostPet_ValidRequestBody_Passes()
    {
        using var content = new StringContent(ValidPetJson, Encoding.UTF8, "application/json");
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidPetJson),
            new HttpRequestMessage(HttpMethod.Post, "/pet") { Content = content }
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task StoreInventory_AdditionalPropertiesMap_Passes()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "available": 5, "pending": 2, "sold": 9 }"""),
            new HttpRequestMessage(HttpMethod.Get, "/store/inventory")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task UndocumentedPath_Throws()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, "{}"),
            new HttpRequestMessage(HttpMethod.Get, "/not/in/spec")
        );

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Location == "path");
    }

    [Fact]
    public async Task UndocumentedMethod_Throws()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidPetJson),
            new HttpRequestMessage(HttpMethod.Patch, "/pet/10")
        );

        outcome.AssertThrown(ContractPhase.Request);
    }

    [Fact]
    public async Task NonIntegerPathParam_Throws()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidPetJson),
            new HttpRequestMessage(HttpMethod.Get, "/pet/not-a-number")
        );

        outcome.AssertThrown(ContractPhase.Request);
    }

    [Fact]
    public async Task FindByStatus_InvalidEnum_Throws()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, $"[{ValidPetJson}]"),
            new HttpRequestMessage(HttpMethod.Get, "/pet/findByStatus?status=teleported")
        );

        outcome.AssertThrown(ContractPhase.Request);
    }

    [Fact]
    public async Task PostPet_MissingRequiredName_Throws()
    {
        using var content = new StringContent(
            """{ "photoUrls": ["http://x/y.png"] }""",
            Encoding.UTF8,
            "application/json"
        );
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidPetJson),
            new HttpRequestMessage(HttpMethod.Post, "/pet") { Content = content }
        );

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Location.StartsWith("requestBody"));
    }

    [Fact]
    public async Task ResponseMissingRequiredPhotoUrls_Throws()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "id": 10, "name": "doggie" }"""),
            new HttpRequestMessage(HttpMethod.Get, "/pet/10")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody");
    }

    [Fact]
    public async Task ResponseWrongFieldType_Throws()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "id": "ten", "name": "doggie", "photoUrls": [] }"""),
            new HttpRequestMessage(HttpMethod.Get, "/pet/10")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.InstanceLocation == "/id");
    }

    [Fact]
    public async Task ResponseInvalidStatusEnum_Throws()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "name": "doggie", "photoUrls": [], "status": "teleported" }"""),
            new HttpRequestMessage(HttpMethod.Get, "/pet/10")
        );

        outcome.AssertThrown(ContractPhase.Response);
    }

    [Fact]
    public async Task DefaultResponse_MatchesUndocumentedStatus_Passes()
    {
        // Every Petstore operation declares a `default` response, so a status with no
        // explicit entry (here 500) legitimately matches `default`. That `default` declares
        // no content, so a bodiless response must NOT throw.
        RequestDelegate terminal = ctx =>
        {
            ctx.Response.StatusCode = 500;
            return Task.CompletedTask;
        };

        var outcome = await PetstoreHosts.SendRawAsync(
            terminal,
            new HttpRequestMessage(HttpMethod.Get, "/pet/10")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.InternalServerError, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task DefaultResponse_WithUndocumentedBody_Throws()
    {
        // The `default` response for GET /pet/{petId} declares no content, so returning a
        // body under it is contract drift and must be rejected.
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(500, """{ "code": 500, "type": "error", "message": "boom" }"""),
            new HttpRequestMessage(HttpMethod.Get, "/pet/10")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody");
    }

    [Fact]
    public async Task WrongResponseContentType_Throws()
    {
        RequestDelegate terminal = async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync("doggie");
        };

        var outcome = await PetstoreHosts.SendRawAsync(
            terminal,
            new HttpRequestMessage(HttpMethod.Get, "/pet/10")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody/contentType");
    }

    [Fact]
    public async Task BadResponse_IsSuppressed_ClientNeverReceivesOffendingBody()
    {
        const string offending = """{ "id": "ten", "name": "doggie", "photoUrls": [] }""";
        var response = await PetstoreHosts.SendProductionAsync(
            Json(200, offending),
            new HttpRequestMessage(HttpMethod.Get, "/pet/10")
        );

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"ten\"", body);
        Assert.Contains("internal server error", body);
    }
}
