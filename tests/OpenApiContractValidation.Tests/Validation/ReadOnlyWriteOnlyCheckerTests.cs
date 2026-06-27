using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using OpenApiContractValidation.Errors;
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
}
