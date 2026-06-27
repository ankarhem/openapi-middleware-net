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

public class ResponseValidatorTests
{
    /// <summary>
    /// Contract for <c>GET /widget</c> (200 with required <c>X-Rate-Limit</c> header + body schema
    /// carrying a <c>writeOnly</c> <c>secret</c>, 204 no content, and a <c>default</c> error shape)
    /// and <c>GET /strict</c> (only a "200" response, so non-2xx statuses are undocumented).
    /// </summary>
    private const string ContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "t", "version": "1" },
          "paths": {
            "/widget": {
              "get": {
                "operationId": "getWidget",
                "responses": {
                  "200": {
                    "description": "ok",
                    "headers": {
                      "X-Rate-Limit": { "required": true, "schema": { "type": "integer" } }
                    },
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "required": ["id"],
                          "properties": {
                            "id": { "type": "integer" },
                            "secret": { "type": "string", "writeOnly": true }
                          }
                        }
                      }
                    }
                  },
                  "204": { "description": "no content" },
                  "default": {
                    "description": "err",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "properties": { "error": { "type": "string" } }
                        }
                      }
                    }
                  }
                }
              }
            },
            "/strict": {
              "get": {
                "operationId": "getStrict",
                "responses": {
                  "200": {
                    "description": "ok",
                    "headers": {
                      "X-Rate-Limit": { "required": true, "schema": { "type": "integer" } }
                    },
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "required": ["id"],
                          "properties": {
                            "id": { "type": "integer" },
                            "secret": { "type": "string", "writeOnly": true }
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

    private static (ContractSchemaRegistry Registry, ResponseValidator Validator) Create()
    {
        var doc = LoadDocument();
        var registry = new ContractSchemaRegistry(doc);
        return (registry, new ResponseValidator(registry));
    }

    private static OpenApiDocument LoadDocument()
    {
        var read = OpenApiDocument.Parse(ContractJson, "json", new OpenApiReaderSettings());
        var diagnostic =
            read.Diagnostic ?? throw new InvalidOperationException("parser produced no diagnostic");
        Assert.Empty(diagnostic.Errors);
        return read.Document ?? throw new InvalidOperationException("parser produced no document");
    }

    private static OpenApiOperation GetOperation(string path)
    {
        var paths = LoadDocument().Paths ?? throw new InvalidOperationException("no paths");
        var item = paths[path] ?? throw new InvalidOperationException($"path {path} not found");
        var operations = item.Operations ?? throw new InvalidOperationException("no operations");
        return operations[HttpMethod.Get];
    }

    private static ParsedResponse Build(
        int status,
        string? contentType = null,
        Dictionary<string, IReadOnlyList<string>>? headers = null,
        string? bodyJson = null
    )
    {
        JsonElement? body = null;
        var hasBody = false;
        if (bodyJson is not null)
        {
            body = JsonDocument.Parse(bodyJson).RootElement.Clone();
            hasBody = true;
        }

        return new ParsedResponse
        {
            StatusCode = status,
            ContentType = contentType,
            Headers = headers ?? new Dictionary<string, IReadOnlyList<string>>(),
            Body = body,
            RawBody = bodyJson,
            HasBody = hasBody,
        };
    }

    private static Dictionary<string, IReadOnlyList<string>> RateLimitHeader(int value) =>
        new() { ["X-Rate-Limit"] = new List<string> { value.ToString() } };

    [Fact]
    public void ValidResponse_Passes()
    {
        var (_, validator) = Create();
        var op = GetOperation("/widget");
        var response = Build(200, "application/json", RateLimitHeader(100), """{"id":1}""");

        var result = validator.Validate(op, response);

        Assert.True(result.IsValid, "status 200 + content-type + header + body all conform");
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void UndocumentedStatus_Violation()
    {
        var (_, validator) = Create();
        var op = GetOperation("/strict");
        var response = Build(500, "application/json");

        var result = validator.Validate(op, response);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Location == "status");
    }

    [Fact]
    public void MissingRequiredResponseHeader_Violation()
    {
        var (_, validator) = Create();
        var op = GetOperation("/widget");
        var response = Build(200, "application/json", headers: new(), bodyJson: """{"id":1}""");

        var result = validator.Validate(op, response);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Location == "responseHeader/X-Rate-Limit");
    }

    [Fact]
    public void BadResponseBodySchema_Violation()
    {
        var (_, validator) = Create();
        var op = GetOperation("/widget");
        var response = Build(200, "application/json", RateLimitHeader(100), """{"id":"notint"}""");

        var result = validator.Validate(op, response);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.InstanceLocation == "/id");
    }

    [Fact]
    public void WriteOnlyInResponse_Violation()
    {
        var (_, validator) = Create();
        var op = GetOperation("/widget");
        var response = Build(
            200,
            "application/json",
            RateLimitHeader(100),
            """{"id":1,"secret":"shh"}"""
        );

        var result = validator.Validate(op, response);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Keyword == "writeOnly");
    }

    [Fact]
    public void Status204_WithBody_Violation()
    {
        var (_, validator) = Create();
        var op = GetOperation("/widget");
        var response = Build(204, bodyJson: """{"id":1}""");

        var result = validator.Validate(op, response);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Location == "responseBody");
    }

    [Fact]
    public void Status204_NoBody_Valid()
    {
        var (_, validator) = Create();
        var op = GetOperation("/widget");
        var response = Build(204);

        var result = validator.Validate(op, response);

        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void DefaultStatusMatched_Valid()
    {
        var (_, validator) = Create();
        var op = GetOperation("/widget");
        var response = Build(503, "application/json", bodyJson: """{"error":"down"}""");

        var result = validator.Validate(op, response);

        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void WrongResponseContentType_Violation()
    {
        var (_, validator) = Create();
        var op = GetOperation("/widget");
        var response = Build(200, "text/plain", RateLimitHeader(100), """{"id":1}""");

        var result = validator.Validate(op, response);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Location == "responseBody/contentType");
    }
}
