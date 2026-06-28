using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenApiContractValidation.Errors;
using OpenApiContractValidation.Middleware;
using OpenApiContractValidation.Models;
using OpenApiContractValidation.Options;
using Xunit;

namespace OpenApiContractValidation.Tests.Middleware;

/// <summary>
/// Direct unit tests for <see cref="OpenApiContractValidator"/>. The validator is constructed via
/// <c>Options.Create(new OpenApiValidationOptions{...})</c> and exercised across every contract-source
/// branch, the startup guards (no/multiple sources, empty paths, streaming content types) and the
/// <see cref="OpenApiContractValidator.TryResolveOperation"/> / delegation surface.
/// </summary>
public class OpenApiContractValidatorTests
{
    /// <summary>
    /// <c>GET /users/{id}</c> (integer path parameter) -> 200 application/json
    /// {id:int required, name:string required}. No requestBody declared.
    /// </summary>
    private const string BaseContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "validator-tests", "version": "1.0.0" },
          "paths": {
            "/users/{id}": {
              "get": {
                "operationId": "getUserById",
                "parameters": [
                  { "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } }
                ],
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "required": ["id", "name"],
                          "properties": {
                            "id": { "type": "integer" },
                            "name": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    /// <summary>A real path alongside an empty path item (<c>"/empty": {}</c>) with no operations.</summary>
    private const string EmptyOperationsContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "validator-tests", "version": "1.0.0" },
          "paths": {
            "/empty": {},
            "/users/{id}": {
              "get": {
                "operationId": "getUserById",
                "parameters": [
                  { "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } }
                ],
                "responses": { "200": { "description": "ok" } }
              }
            }
          }
        }
        """;

    private const string StreamingRequestBodyContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "validator-tests", "version": "1.0.0" },
          "paths": {
            "/stream": {
              "post": {
                "operationId": "streamIn",
                "requestBody": { "content": { "text/event-stream": {} } },
                "responses": { "200": { "description": "ok" } }
              }
            }
          }
        }
        """;

    private const string StreamingResponseContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "validator-tests", "version": "1.0.0" },
          "paths": {
            "/stream": {
              "get": {
                "operationId": "streamOut",
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": { "text/event-stream": {} }
                  }
                }
              }
            }
          }
        }
        """;

    private const string EmptyPathsContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "validator-tests", "version": "1.0.0" },
          "paths": {}
        }
        """;

    /// <summary>OpenAPI document with no top-level <c>paths</c> key at all (exercises the null guard).</summary>
    private const string NoPathsKeyContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "validator-tests", "version": "1.0.0" }
        }
        """;

    /// <summary>
    /// A POST whose <c>requestBody</c> is declared but carries no <c>content</c> map: this drives the
    /// <c>RequestBody?.Content?.Keys</c> null-conditional branch in <c>RejectStreamingContent</c> without
    /// tripping any streaming content type.
    /// </summary>
    private const string RequestBodyWithoutContentContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "validator-tests", "version": "1.0.0" },
          "paths": {
            "/things": {
              "post": {
                "operationId": "createThing",
                "requestBody": { "required": false },
                "responses": { "200": { "description": "ok" } }
              }
            }
          }
        }
        """;

    /// <summary>Builds a validator from inline JSON contract text.</summary>
    private static OpenApiContractValidator Create(string contractText) =>
        new(
            Microsoft.Extensions.Options.Options.Create(
                new OpenApiValidationOptions
                {
                    ContractText = contractText,
                    ContractFormat = "json",
                }
            )
        );

    [Fact]
    public void Ctor_NullOptions_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenApiContractValidator(null!));
    }

    [Fact]
    public void Ctor_NoSourceConfigured_ThrowsStartup_NoContractSource()
    {
        var ex = Assert.Throws<OpenApiContractValidationException>(() =>
            new OpenApiContractValidator(
                Microsoft.Extensions.Options.Options.Create(new OpenApiValidationOptions())
            )
        );
        Assert.Equal(ContractPhase.Startup, ex.Phase);
        Assert.Contains("No OpenAPI contract source", ex.Message);
    }

    [Fact]
    public void Ctor_MultipleSourcesConfigured_ThrowsStartup_MultipleSources()
    {
        var ex = Assert.Throws<OpenApiContractValidationException>(() =>
            new OpenApiContractValidator(
                Microsoft.Extensions.Options.Options.Create(
                    new OpenApiValidationOptions
                    {
                        ContractText = "{}",
                        ContractFilePath = "/nonexistent/path.json",
                    }
                )
            )
        );
        Assert.Equal(ContractPhase.Startup, ex.Phase);
        Assert.Contains("Multiple OpenAPI contract sources", ex.Message);
    }

    [Fact]
    public void Ctor_ContractText_ConstructsOk_AndOptionsReturnsSameInstance()
    {
        var options = new OpenApiValidationOptions
        {
            ContractText = BaseContractJson,
            ContractFormat = "json",
        };

        var validator = new OpenApiContractValidator(
            Microsoft.Extensions.Options.Options.Create(options)
        );

        Assert.Same(options, validator.Options);
    }

    [Fact]
    public void Ctor_ContractStream_ConstructsOk()
    {
        var bytes = Encoding.UTF8.GetBytes(BaseContractJson);
        using var stream = new MemoryStream(bytes);

        var validator = new OpenApiContractValidator(
            Microsoft.Extensions.Options.Options.Create(
                new OpenApiValidationOptions { ContractStream = stream, ContractFormat = "json" }
            )
        );

        Assert.NotNull(validator.Options);
    }

    [Fact]
    public void Ctor_ContractFilePath_ConstructsOk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openapi-contract-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, BaseContractJson);
        try
        {
            var validator = new OpenApiContractValidator(
                Microsoft.Extensions.Options.Options.Create(
                    new OpenApiValidationOptions { ContractFilePath = path }
                )
            );

            Assert.NotNull(validator.Options);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Ctor_EmptyPaths_ThrowsStartup_NoPaths()
    {
        var ex = Assert.Throws<OpenApiContractValidationException>(() =>
            Create(EmptyPathsContractJson)
        );

        Assert.Equal(ContractPhase.Startup, ex.Phase);
        Assert.Contains("declares no paths", ex.Message);
    }

    [Fact]
    public void Ctor_NoPathsKey_ThrowsStartup_NoPaths()
    {
        var ex = Assert.Throws<OpenApiContractValidationException>(() =>
            Create(NoPathsKeyContractJson)
        );

        Assert.Equal(ContractPhase.Startup, ex.Phase);
    }

    [Fact]
    public void Ctor_RequestBodyWithoutContent_ConstructsOk()
    {
        // requestBody declared with no content map drives the Content-null null-conditional branch in
        // RejectStreamingContent; it must not be misread as streaming and must not throw.
        var validator = Create(RequestBodyWithoutContentContractJson);

        Assert.NotNull(validator.Options);
    }

    [Fact]
    public void Ctor_StreamingRequestBody_ThrowsStartup()
    {
        var ex = Assert.Throws<OpenApiContractValidationException>(() =>
            Create(StreamingRequestBodyContractJson)
        );

        Assert.Equal(ContractPhase.Startup, ex.Phase);
        Assert.Contains("Streaming content type", ex.Message);
    }

    [Fact]
    public void Ctor_StreamingResponse_ThrowsStartup()
    {
        var ex = Assert.Throws<OpenApiContractValidationException>(() =>
            Create(StreamingResponseContractJson)
        );

        Assert.Equal(ContractPhase.Startup, ex.Phase);
        Assert.Contains("Streaming content type", ex.Message);
    }

    [Fact]
    public void Ctor_EmptyOperationsPath_ConstructsOk_AndTryResolveReturnsNullOperation()
    {
        var validator = Create(EmptyOperationsContractJson);

        var ok = validator.TryResolveOperation(
            "GET",
            "/empty",
            out var operation,
            out var pathParameters,
            out var pathExists
        );

        Assert.True(ok);
        Assert.True(pathExists);
        Assert.Null(operation);
        Assert.NotNull(pathParameters);
    }

    [Fact]
    public void TryResolve_NullMethod_Throws()
    {
        var validator = Create(BaseContractJson);

        Assert.Throws<ArgumentNullException>(() =>
            validator.TryResolveOperation(null!, "/users/1", out _, out _, out _)
        );
    }

    [Fact]
    public void TryResolve_NullPath_Throws()
    {
        var validator = Create(BaseContractJson);

        Assert.Throws<ArgumentNullException>(() =>
            validator.TryResolveOperation("GET", null!, out _, out _, out _)
        );
    }

    [Fact]
    public void TryResolve_UnmatchedPath_ReturnsPathExistsFalse_AndNullOperation()
    {
        var validator = Create(BaseContractJson);

        validator.TryResolveOperation("GET", "/nope", out var operation, out _, out var pathExists);

        Assert.False(pathExists);
        Assert.Null(operation);
    }

    [Fact]
    public void TryResolve_MatchedPath_UndocumentedMethod_ReturnsNullOperation_AndCapturesParameters()
    {
        var validator = Create(BaseContractJson);

        validator.TryResolveOperation(
            "DELETE",
            "/users/1",
            out var operation,
            out var pathParameters,
            out var pathExists
        );

        Assert.True(pathExists);
        Assert.Null(operation);
        Assert.Equal("1", pathParameters["id"]);
    }

    [Fact]
    public void TryResolve_MatchedPath_DocumentedMethod_CaseInsensitive_ReturnsOperation()
    {
        var validator = Create(BaseContractJson);

        validator.TryResolveOperation(
            "get",
            "/users/1",
            out var operation,
            out var pathParameters,
            out _
        );

        Assert.NotNull(operation);
        Assert.Equal("1", pathParameters["id"]);
    }

    [Fact]
    public void TryResolve_MatchedPath_DocumentedMethod_ExactCase_ReturnsOperation()
    {
        var validator = Create(BaseContractJson);

        validator.TryResolveOperation("GET", "/users/1", out var operation, out _, out _);

        Assert.NotNull(operation);
    }

    [Fact]
    public void ValidateRequest_ConformantRequest_DelegatesAndReturnsValid()
    {
        var validator = Create(BaseContractJson);
        validator.TryResolveOperation(
            "GET",
            "/users/1",
            out var operation,
            out var pathParameters,
            out _
        );

        var request = new ParsedRequest
        {
            Method = "GET",
            Path = "/users/1",
            ContentType = null,
            QueryValues = new Dictionary<string, IReadOnlyList<string>>(),
            Headers = new Dictionary<string, IReadOnlyList<string>>(),
            Cookies = new Dictionary<string, string>(),
            Body = null,
            RawBody = null,
        };

        var result = validator.ValidateRequest(operation!, request, pathParameters);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateResponse_ConformantResponse_DelegatesAndReturnsValid()
    {
        var validator = Create(BaseContractJson);
        validator.TryResolveOperation("GET", "/users/1", out var operation, out _, out _);

        var body = JsonDocument.Parse("""{"id":1,"name":"x"}""").RootElement.Clone();
        var response = new ParsedResponse
        {
            StatusCode = 200,
            ContentType = "application/json",
            Headers = new Dictionary<string, IReadOnlyList<string>>(),
            Body = body,
            RawBody = """{"id":1,"name":"x"}""",
            HasBody = true,
        };

        var result = validator.ValidateResponse(operation!, response);

        Assert.True(result.IsValid);
    }
}
