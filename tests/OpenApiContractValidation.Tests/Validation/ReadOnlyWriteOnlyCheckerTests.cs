using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenApiContractValidation.Validation;
using Xunit;

namespace OpenApiContractValidation.Tests.Validation;

public class ReadOnlyWriteOnlyCheckerTests
{
    /// <summary>
    /// Schema "User": object with <c>id</c> (readOnly integer), <c>name</c> (string),
    /// <c>password</c> (writeOnly string). Reused across the request/response scenarios.
    /// </summary>
    private static OpenApiSchema CreateUserSchema() =>
        new()
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["id"] = new OpenApiSchema { Type = JsonSchemaType.Integer, ReadOnly = true },
                ["name"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["password"] = new OpenApiSchema { Type = JsonSchemaType.String, WriteOnly = true },
            },
        };

    [Fact]
    public void Request_WithReadOnlyProperty_Violation()
    {
        var schema = CreateUserSchema();
        var instance = JsonNode.Parse("""{"id":1,"name":"x"}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        var violation = Assert.Single(violations);
        Assert.Equal("requestBody", violation.Location);
        Assert.Equal("/id", violation.InstanceLocation);
        Assert.Equal("readOnly", violation.Keyword);
        Assert.Contains("id", violation.Message);
        Assert.Contains("readOnly", violation.Message);
    }

    [Fact]
    public void Request_WithoutReadOnly_NoViolation()
    {
        var schema = CreateUserSchema();
        var instance = JsonNode.Parse("""{"name":"x"}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        Assert.Empty(violations);
    }

    [Fact]
    public void Response_WithWriteOnlyProperty_Violation()
    {
        var schema = CreateUserSchema();
        var instance = JsonNode.Parse("""{"id":1,"name":"x","password":"secret"}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckResponse(schema, instance, "responseBody");

        var violation = Assert.Single(violations);
        Assert.Equal("responseBody", violation.Location);
        Assert.Equal("/password", violation.InstanceLocation);
        Assert.Equal("writeOnly", violation.Keyword);
        Assert.Contains("password", violation.Message);
        Assert.Contains("writeOnly", violation.Message);
    }

    [Fact]
    public void Response_WithoutWriteOnly_NoViolation()
    {
        var schema = CreateUserSchema();
        var instance = JsonNode.Parse("""{"id":1,"name":"x"}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckResponse(schema, instance, "responseBody");

        Assert.Empty(violations);
    }

    [Fact]
    public void Nested_Object_ReadOnly()
    {
        var orderSchema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["customer"] = CreateUserSchema(),
            },
        };
        var instance = JsonNode.Parse("""{"customer":{"id":5,"name":"y"}}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(
            orderSchema,
            instance,
            "requestBody"
        );

        var violation = Assert.Single(violations);
        Assert.Equal("/customer/id", violation.InstanceLocation);
        Assert.Equal("readOnly", violation.Keyword);
    }

    [Fact]
    public void Array_Items_WriteOnly()
    {
        var listSchema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["users"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = CreateUserSchema(),
                },
            },
        };
        var instance = JsonNode.Parse("""{"users":[{"id":1,"name":"a","password":"p"}]}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckResponse(
            listSchema,
            instance,
            "responseBody"
        );

        var violation = Assert.Single(violations);
        Assert.Equal("/users/0/password", violation.InstanceLocation);
        Assert.Equal("writeOnly", violation.Keyword);
    }

    [Fact]
    public void Dedup_AllOf_TwoBranchesSameReadOnlyProp_ReturnsSingleViolation()
    {
        var schema = new OpenApiSchema
        {
            AllOf = new List<IOpenApiSchema>
            {
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["id"] = new OpenApiSchema { ReadOnly = true },
                    },
                },
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["id"] = new OpenApiSchema { ReadOnly = true },
                    },
                },
            },
        };
        var instance = JsonNode.Parse("""{"id":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        var violation = Assert.Single(violations);
        Assert.Equal("/id", violation.InstanceLocation);
        Assert.Equal("readOnly", violation.Keyword);
    }

    [Fact]
    public void NullSchema_ReturnsEmpty_NoThrow()
    {
        var instance = JsonNode.Parse("""{"id":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(null!, instance, "requestBody");

        Assert.Empty(violations);
    }

    [Fact]
    public void NullPropertySchema_NoThrow()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema> { ["x"] = null! },
        };
        var instance = JsonNode.Parse("""{"x":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        Assert.Empty(violations);
    }

    [Fact]
    public void AllOf_ReadOnlyProperty_Violation()
    {
        var schema = new OpenApiSchema
        {
            AllOf = new List<IOpenApiSchema>
            {
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["id"] = new OpenApiSchema { ReadOnly = true },
                    },
                },
            },
        };
        var instance = JsonNode.Parse("""{"id":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        var violation = Assert.Single(violations);
        Assert.Equal("/id", violation.InstanceLocation);
        Assert.Equal("readOnly", violation.Keyword);
    }

    [Fact]
    public void OneOf_ReadOnlyProperty_Violation()
    {
        var schema = new OpenApiSchema
        {
            OneOf = new List<IOpenApiSchema>
            {
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["id"] = new OpenApiSchema { ReadOnly = true },
                    },
                },
            },
        };
        var instance = JsonNode.Parse("""{"id":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        var violation = Assert.Single(violations);
        Assert.Equal("/id", violation.InstanceLocation);
        Assert.Equal("readOnly", violation.Keyword);
    }

    [Fact]
    public void AnyOf_ReadOnlyProperty_Violation()
    {
        var schema = new OpenApiSchema
        {
            AnyOf = new List<IOpenApiSchema>
            {
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["id"] = new OpenApiSchema { ReadOnly = true },
                    },
                },
            },
        };
        var instance = JsonNode.Parse("""{"id":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        var violation = Assert.Single(violations);
        Assert.Equal("/id", violation.InstanceLocation);
        Assert.Equal("readOnly", violation.Keyword);
    }

    [Fact]
    public void Array_NullElement_Skipped_WriteOnlyInNext_Violation()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Array,
            Items = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["secret"] = new OpenApiSchema { WriteOnly = true },
                },
            },
        };
        var instance = JsonNode.Parse("""[null,{"secret":"x"}]""");

        var violations = ReadOnlyWriteOnlyChecker.CheckResponse(schema, instance, "responseBody");

        var violation = Assert.Single(violations);
        Assert.Equal("/1/secret", violation.InstanceLocation);
        Assert.Equal("writeOnly", violation.Keyword);
    }

    [Fact]
    public void HasFlag_AllOf_TransitiveReadOnly_Violation()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["id"] = new OpenApiSchema
                {
                    AllOf = new List<IOpenApiSchema> { new OpenApiSchema { ReadOnly = true } },
                },
            },
        };
        var instance = JsonNode.Parse("""{"id":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        var violation = Assert.Single(violations);
        Assert.Equal("/id", violation.InstanceLocation);
        Assert.Equal("readOnly", violation.Keyword);
    }

    [Fact]
    public void HasFlag_OneOf_TransitiveReadOnly_Violation()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["id"] = new OpenApiSchema
                {
                    OneOf = new List<IOpenApiSchema> { new OpenApiSchema { ReadOnly = true } },
                },
            },
        };
        var instance = JsonNode.Parse("""{"id":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        var violation = Assert.Single(violations);
        Assert.Equal("/id", violation.InstanceLocation);
        Assert.Equal("readOnly", violation.Keyword);
    }

    [Fact]
    public void HasFlag_OneOf_NoMatch_NoViolation()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["id"] = new OpenApiSchema
                {
                    OneOf = new List<IOpenApiSchema>
                    {
                        new OpenApiSchema { Type = JsonSchemaType.String },
                    },
                },
            },
        };
        var instance = JsonNode.Parse("""{"id":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        Assert.Empty(violations);
    }

    [Fact]
    public void HasFlag_AnyOf_TransitiveReadOnly_Violation()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["id"] = new OpenApiSchema
                {
                    AnyOf = new List<IOpenApiSchema> { new OpenApiSchema { ReadOnly = true } },
                },
            },
        };
        var instance = JsonNode.Parse("""{"id":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        var violation = Assert.Single(violations);
        Assert.Equal("/id", violation.InstanceLocation);
        Assert.Equal("readOnly", violation.Keyword);
    }

    [Fact]
    public void HasFlag_AnyOf_NoMatch_NoViolation()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["id"] = new OpenApiSchema
                {
                    AnyOf = new List<IOpenApiSchema>
                    {
                        new OpenApiSchema { Type = JsonSchemaType.String },
                    },
                },
            },
        };
        var instance = JsonNode.Parse("""{"id":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        Assert.Empty(violations);
    }

    // Instance value is deliberately null: Walk would infinite-loop on the
    // self-referential allOf, but IsReadOnly still runs first (exercising the
    // HasFlag visited-set cycle guard).
    [Fact]
    public void HasFlag_SelfReferencingAllOf_TerminatesWithoutThrowing()
    {
        var recursive = new OpenApiSchema();
        recursive.AllOf = new List<IOpenApiSchema> { recursive };

        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema> { ["x"] = recursive },
        };
        var instance = JsonNode.Parse("""{"x":null}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        Assert.Empty(violations);
    }

    [Fact]
    public void Resolve_NormalReference_FollowsToReadOnlyTarget()
    {
        var read = OpenApiDocument.Parse(
            """
            {
              "openapi": "3.1.0",
              "info": { "title": "t", "version": "1" },
              "paths": {},
              "components": {
                "schemas": {
                  "Root": {
                    "type": "object",
                    "properties": {
                      "id": { "$ref": "#/components/schemas/Target" }
                    }
                  },
                  "Target": { "type": "integer", "readOnly": true }
                }
              }
            }
            """,
            "json",
            new OpenApiReaderSettings()
        );
        var root = read.Document!.Components!.Schemas!["Root"]!;
        var instance = JsonNode.Parse("""{"id":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(root, instance, "requestBody");

        var violation = Assert.Single(violations);
        Assert.Equal("/id", violation.InstanceLocation);
        Assert.Equal("readOnly", violation.Keyword);
    }

    [Fact]
    public void Resolve_DanglingReference_BreaksGracefully()
    {
        var read = OpenApiDocument.Parse(
            """
            {
              "openapi": "3.1.0",
              "info": { "title": "t", "version": "1" },
              "paths": {},
              "components": {
                "schemas": {
                  "Root": {
                    "type": "object",
                    "properties": {
                      "x": { "$ref": "#/components/schemas/Missing" }
                    }
                  }
                }
              }
            }
            """,
            "json",
            new OpenApiReaderSettings()
        );
        var root = read.Document!.Components!.Schemas!["Root"]!;
        var instance = JsonNode.Parse("""{"x":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(root, instance, "requestBody");

        Assert.Empty(violations);
    }

    [Fact]
    public void EscapeToken_PropertyNameWithSlashAndTilde()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["a/b~c"] = new OpenApiSchema { ReadOnly = true },
            },
        };
        var instance = JsonNode.Parse("""{"a/b~c":1}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckRequest(schema, instance, "requestBody");

        var violation = Assert.Single(violations);
        Assert.Equal("/a~1b~0c", violation.InstanceLocation);
    }

    [Fact]
    public void Response_AllOf_WriteOnly_Violation()
    {
        var schema = new OpenApiSchema
        {
            AllOf = new List<IOpenApiSchema>
            {
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["secret"] = new OpenApiSchema { WriteOnly = true },
                    },
                },
            },
        };
        var instance = JsonNode.Parse("""{"secret":"x"}""");

        var violations = ReadOnlyWriteOnlyChecker.CheckResponse(schema, instance, "responseBody");

        var violation = Assert.Single(violations);
        Assert.Equal("/secret", violation.InstanceLocation);
        Assert.Equal("writeOnly", violation.Keyword);
    }
}
