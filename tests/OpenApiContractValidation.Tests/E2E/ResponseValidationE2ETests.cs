using System.Net;
using Microsoft.AspNetCore.Http;
using OpenApiContractValidation.Errors;
using OpenApiContractValidation.Models;
using Xunit;

namespace OpenApiContractValidation.Tests.E2E;

/// <summary>
/// Response-validation E2E tests (scenarios 14-19). These use inline <see cref="TestServer"/> hosts
/// that reuse the SampleApi contract but wire terminal endpoints emitting deliberately-malformed
/// responses, proving the middleware throws during the Response phase and (for scenario 14) that the
/// offending body is suppressed before it reaches the client.
/// </summary>
public class ResponseValidationE2ETests
{
    [Fact]
    public async Task ResponseBodySchemaViolation_ThrowsResponsePhase()
    {
        var outcome = await E2EHosts.SendRawAsync(
            async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"id":"notInt","name":"x"}""");
            },
            new HttpRequestMessage(HttpMethod.Get, "/users/1")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody" && v.Keyword == "type");
    }

    [Fact]
    public async Task WriteOnlyPasswordInResponse_ThrowsResponsePhase()
    {
        var outcome = await E2EHosts.SendRawAsync(
            async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"id":1,"name":"x","password":"leak"}""");
            },
            new HttpRequestMessage(HttpMethod.Get, "/users/1")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(
            ex.Violations,
            v => v.Keyword == "writeOnly" && v.InstanceLocation == "/password"
        );
    }

    [Fact]
    public async Task ExtraPropertyInResponse_ThrowsResponsePhase()
    {
        var outcome = await E2EHosts.SendRawAsync(
            async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"id":1,"name":"x","bogus":"extra"}""");
            },
            new HttpRequestMessage(HttpMethod.Get, "/users/1")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(
            ex.Violations,
            v => v.Location == "responseBody" && v.InstanceLocation == "/bogus"
        );
    }

    [Fact]
    public async Task UndocumentedResponseStatus_ThrowsResponsePhase()
    {
        var outcome = await E2EHosts.SendRawAsync(
            async ctx =>
            {
                ctx.Response.StatusCode = 418;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"iAm":"teapot"}""");
            },
            new HttpRequestMessage(HttpMethod.Get, "/users/1")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "status" && v.Actual == "418");
    }

    [Fact]
    public async Task BodyOn204Response_ThrowsResponsePhase()
    {
        var outcome = await E2EHosts.SendRawAsync(
            async ctx =>
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"unexpected":"body"}""");
            },
            new HttpRequestMessage(HttpMethod.Delete, "/no-content")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(
            ex.Violations,
            v => v.Location == "responseBody" && v.Message.Contains("must not contain a body")
        );
    }

    [Fact]
    public async Task WrongResponseContentType_ThrowsResponsePhase()
    {
        var outcome = await E2EHosts.SendRawAsync(
            async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.WriteAsync("just text");
            },
            new HttpRequestMessage(HttpMethod.Get, "/users/1")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody/contentType");
    }

    /// <summary>
    /// Proves bad-body SUPPRESSION: with an exception handler installed above the validation
    /// middleware, a response schema violation surfaces as a clean 500 whose body is the handler's
    /// generic error — NEVER the offending payload that the endpoint tried to send.
    /// </summary>
    [Fact]
    public async Task BadBody_IsSuppressed_ClientGets500WithoutOffendingPayload()
    {
        const string badPayload = """{"id":"notInt","name":"x"}""";

        using var response = await E2EHosts.SendProductionAsync(
            async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(badPayload);
            },
            new HttpRequestMessage(HttpMethod.Get, "/users/1")
        );

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("notInt", body);
        Assert.DoesNotContain(badPayload, body);
    }
}
