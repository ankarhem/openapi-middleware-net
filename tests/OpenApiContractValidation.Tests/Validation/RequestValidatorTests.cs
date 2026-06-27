using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenApiContractValidation.Models;
using OpenApiContractValidation.Schema;
using OpenApiContractValidation.Validation;
using Xunit;

namespace OpenApiContractValidation.Tests.Validation;

public class RequestValidatorTests
{
    /// <summary>
    /// Contract exercising query parameters (a required integer, an optional string enum)
    /// and a required JSON request body whose schema declares a required <c>name</c>
    /// and a <c>readOnly</c> integer <c>id</c>.
    /// </summary>
    private const string ContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "t", "version": "1" },
          "paths": {
            "/users": {
              "post": {
                "operationId": "createUser",
                "parameters": [
                  { "name": "userId", "in": "query", "required": true, "schema": { "type": "integer" } },
                  { "name": "status", "in": "query", "schema": { "type": "string", "enum": ["active","inactive"] } }
                ],
                "requestBody": {
                  "required": true,
                  "content": { "application/json": { "schema": { "$ref": "#/components/schemas/User" } } }
                },
                "responses": { "201": { "description": "ok" } }
              }
            }
          },
          "components": {
            "schemas": {
              "User": {
                "type": "object",
                "required": ["name"],
                "properties": {
                  "name": { "type": "string" },
                  "id": { "type": "integer", "readOnly": true }
                }
              }
            }
          }
        }
        """;

    private readonly OpenApiOperation _operation;
    private readonly RequestValidator _validator;
    private readonly IReadOnlyDictionary<string, string> _noPathParameters =
        new Dictionary<string, string>();

    public RequestValidatorTests()
    {
        var read = OpenApiDocument.Parse(ContractJson, "json", new OpenApiReaderSettings());
        var diagnostic =
            read.Diagnostic ?? throw new InvalidOperationException("parser produced no diagnostic");
        Assert.Empty(diagnostic.Errors);
        var doc =
            read.Document ?? throw new InvalidOperationException("parser produced no document");
        _operation = doc.Paths!["/users"].Operations![HttpMethod.Post];
        _validator = new RequestValidator(new ContractSchemaRegistry(doc));
    }

    /// <summary>Builds a POST /users <see cref="ParsedRequest"/> for the given inputs.</summary>
    private static ParsedRequest MakeRequest(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? query = null,
        string? contentType = null,
        string? bodyJson = null
    ) =>
        new()
        {
            Method = "POST",
            Path = "/users",
            ContentType = contentType,
            QueryValues = query ?? new Dictionary<string, IReadOnlyList<string>>(),
            Headers = new Dictionary<string, IReadOnlyList<string>>(),
            Cookies = new Dictionary<string, string>(),
            Body = bodyJson is null ? null : JsonDocument.Parse(bodyJson).RootElement,
            RawBody = bodyJson,
        };

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Query(
        string name,
        string value
    ) => new Dictionary<string, IReadOnlyList<string>> { [name] = new List<string> { value } };

    [Fact]
    public void ValidRequest_Passes()
    {
        var request = MakeRequest(
            query: Query("userId", "5"),
            contentType: "application/json",
            bodyJson: """{"name":"x"}"""
        );

        var result = _validator.Validate(_operation, request, _noPathParameters);

        Assert.True(result.IsValid, string.Join("; ", result.Violations.Select(v => v.ToString())));
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void MissingRequiredQueryParam_Violation()
    {
        // userId omitted (it is required); body is valid so the only violation is the query param.
        var request = MakeRequest(contentType: "application/json", bodyJson: """{"name":"x"}""");

        var result = _validator.Validate(_operation, request, _noPathParameters);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Location == "query/userId");
    }

    [Fact]
    public void InvalidEnum_Violation()
    {
        var request = MakeRequest(
            query: new Dictionary<string, IReadOnlyList<string>>
            {
                ["userId"] = new List<string> { "5" },
                ["status"] = new List<string> { "bogus" },
            },
            contentType: "application/json",
            bodyJson: """{"name":"x"}"""
        );

        var result = _validator.Validate(_operation, request, _noPathParameters);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Violations,
            v => v.Location == "query/status" && v.Keyword == "enum"
        );
    }

    [Fact]
    public void WrongRequestContentType_Violation()
    {
        var request = MakeRequest(
            query: Query("userId", "5"),
            contentType: "text/plain",
            bodyJson: """{"name":"x"}"""
        );

        var result = _validator.Validate(_operation, request, _noPathParameters);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Violations,
            v =>
                v.Location.Contains("requestBody")
                && v.Location.Contains("contentType", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void RequiredBodyMissing_Violation()
    {
        // No body at all (Body is null) while requestBody.required is true.
        var request = MakeRequest(query: Query("userId", "5"));

        var result = _validator.Validate(_operation, request, _noPathParameters);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Violations,
            v =>
                v.Location == "requestBody"
                && v.Message.Contains("required", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void BadBodySchema_Violation()
    {
        // name must be a string; supplying a number must surface a type violation at /name.
        var request = MakeRequest(
            query: Query("userId", "5"),
            contentType: "application/json",
            bodyJson: """{"name":123}"""
        );

        var result = _validator.Validate(_operation, request, _noPathParameters);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Violations,
            v => v.Location == "requestBody" && v.InstanceLocation == "/name" && v.Keyword == "type"
        );
    }

    [Fact]
    public void ReadOnlyInRequest_Violation()
    {
        // 'id' is readOnly; its presence in a request body must be flagged.
        var request = MakeRequest(
            query: Query("userId", "5"),
            contentType: "application/json",
            bodyJson: """{"name":"x","id":7}"""
        );

        var result = _validator.Validate(_operation, request, _noPathParameters);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Keyword == "readOnly");
    }

    [Fact]
    public void QueryParamTypeMismatch_Violation()
    {
        // userId must be an integer; "abc" cannot be coerced to one.
        var request = MakeRequest(
            query: Query("userId", "abc"),
            contentType: "application/json",
            bodyJson: """{"name":"x"}"""
        );

        var result = _validator.Validate(_operation, request, _noPathParameters);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Violations,
            v => v.Location == "query/userId" && v.Keyword == "type"
        );
    }
}
