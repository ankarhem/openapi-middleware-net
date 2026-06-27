using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenApiContractValidation.Errors;
using OpenApiContractValidation.Models;
using Xunit;

namespace OpenApiContractValidation.Tests.E2E;

/// <summary>
/// Request-validation E2E tests (scenarios 7-13). Each drives a contract-violating request through a
/// raw inline <see cref="TestServer"/> host (no exception handler) so the
/// <see cref="OpenApiContractValidationException"/> is rethrown to the caller, allowing precise
/// <see cref="OpenApiContractValidationException.Phase"/> and
/// <see cref="ContractViolation.Keyword"/> assertions.
/// </summary>
public class RequestValidationE2ETests
{
    private static Task<E2EHosts.RawOutcome> SendAsync(HttpRequestMessage request) =>
        E2EHosts.SendRawAsync(NoBody, request);

    private static Task NoBody(HttpContext _) => Task.CompletedTask;

    [Fact]
    public async Task UndocumentedPath_ThrowsRequestPhase()
    {
        var outcome = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "/nope"));

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Location == "path");
    }

    [Fact]
    public async Task UndocumentedMethod_ThrowsRequestPhase()
    {
        var outcome = await SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/users/1"));

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Location == "path" && v.Actual == "PATCH");
    }

    [Fact]
    public async Task MissingRequiredRequestBodyProperty_ThrowsRequestPhase()
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var outcome = await SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/users") { Content = content }
        );

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Keyword == "required" && v.Location == "requestBody");
    }

    [Fact]
    public async Task ReadOnlyIdInRequestBody_ThrowsRequestPhase()
    {
        using var content = new StringContent(
            """{"name":"y","id":5}""",
            Encoding.UTF8,
            "application/json"
        );
        var outcome = await SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/users") { Content = content }
        );

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Keyword == "readOnly");
    }

    [Fact]
    public async Task NonIntegerPathParam_ThrowsRequestPhase()
    {
        var outcome = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "/users/abc"));

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Location == "path/id" && v.Keyword == "type");
    }

    [Fact]
    public async Task EnumQueryViolation_ThrowsRequestPhase()
    {
        var outcome = await SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/users?status=bogus")
        );

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Location == "query/status" && v.Keyword == "enum");
    }

    [Fact]
    public async Task WrongRequestContentType_ThrowsRequestPhase()
    {
        using var content = new StringContent("plain text", Encoding.UTF8, "text/plain");
        var outcome = await SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/users") { Content = content }
        );

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Location == "requestBody/contentType");
    }
}
