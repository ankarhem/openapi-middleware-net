using System.Text.Json;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenApiContractValidation.Schema;
using Xunit;

namespace OpenApiContractValidation.Tests.Schema;

public class ContractSchemaRegistryTests
{
    private const string ContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "t", "version": "1" },
          "paths": {},
          "components": {
            "schemas": {
              "Node": {
                "type": "object",
                "properties": {
                  "value": { "type": "integer" },
                  "children": { "type": "array", "items": { "$ref": "#/components/schemas/Node" } }
                }
              },
              "A": { "type": "object", "properties": { "b": { "$ref": "#/components/schemas/B" } } },
              "B": { "type": "object", "properties": { "c": { "$ref": "#/components/schemas/C" } } },
              "C": {
                "type": "object",
                "properties": { "name": { "type": "string" } },
                "required": [ "name" ]
              }
            }
          }
        }
        """;

    private static (OpenApiDocument Document, ContractSchemaRegistry Registry) Create()
    {
        var read = OpenApiDocument.Parse(ContractJson, "json", new OpenApiReaderSettings());
        var diagnostic =
            read.Diagnostic ?? throw new InvalidOperationException("parser produced no diagnostic");
        Assert.Empty(diagnostic.Errors);
        var doc =
            read.Document ?? throw new InvalidOperationException("parser produced no document");
        return (doc, new ContractSchemaRegistry(doc));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void RecursiveSchema_ValidInstance_Passes()
    {
        var (doc, registry) = Create();
        var nodeSchema = doc.Components!.Schemas!["Node"];

        var schema = registry.GetTargetSchema("Node", nodeSchema);
        var instance = Parse("""{"value":1,"children":[{"value":2,"children":[]}]}""");

        var result = registry.Validate(schema, instance, locationLabel: "responseBody");

        Assert.True(result.IsValid, "nested recursive tree should validate");
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void RecursiveSchema_InvalidInstance_ProducesViolationWithInstanceLocation()
    {
        var (doc, registry) = Create();
        var nodeSchema = doc.Components!.Schemas!["Node"];

        var schema = registry.GetTargetSchema("Node", nodeSchema);
        var instance = Parse("""{"value":"notAnInt","children":[]}""");

        var result = registry.Validate(schema, instance, locationLabel: "responseBody");

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.InstanceLocation == "/value");
        // The failing keyword must be carried through so callers can report it.
        Assert.Contains(result.Violations, v => v.Keyword == "type");
        // The supplied location label is echoed on every violation.
        Assert.All(result.Violations, v => Assert.Equal("responseBody", v.Location));
    }

    [Fact]
    public void RefChain_A_B_C_Validated()
    {
        var (doc, registry) = Create();
        var aSchema = doc.Components!.Schemas!["A"];
        var schema = registry.GetTargetSchema("A", aSchema);

        // C is missing its required "name" property -> must fail somewhere under /b/c.
        var missingName = registry.Validate(
            schema,
            Parse("""{"b":{"c":{}}}"""),
            locationLabel: "responseBody"
        );

        Assert.False(missingName.IsValid);
        Assert.Contains(
            missingName.Violations,
            v =>
                v.InstanceLocation is not null
                && v.InstanceLocation.StartsWith("/b/c")
                && v.Keyword == "required"
        );

        // A fully populated chain -> must pass.
        var valid = registry.Validate(
            schema,
            Parse("""{"b":{"c":{"name":"ok"}}}"""),
            locationLabel: "responseBody"
        );

        Assert.True(valid.IsValid, "fully populated A->B->C chain should validate");
    }

    [Fact]
    public void GetTargetSchema_SameKey_ReturnsCachedInstance()
    {
        var (doc, registry) = Create();
        var nodeSchema = doc.Components!.Schemas!["Node"];

        var first = registry.GetTargetSchema("Node", nodeSchema);
        var second = registry.GetTargetSchema("Node", nodeSchema);

        Assert.True(
            ReferenceEquals(first, second),
            "the same cache key must return the identical cached JsonSchema instance"
        );
    }

    [Fact]
    public void NullSchemaValue_IsSkipped_InBuildRootSchema()
    {
        var read = OpenApiDocument.Parse(ContractJson, "json", new OpenApiReaderSettings());
        var doc = read.Document!;
        doc.Components!.Schemas!.Add("NullEntry", null!);

        var registry = new ContractSchemaRegistry(doc);

        Assert.NotNull(registry.RootSchema);
    }
}

public class RefRewriterTests
{
    [Fact]
    public void RewriteToLocal_RewritesComponentSchemaRefs()
    {
        const string input = """
            { "items": { "$ref": "#/components/schemas/Node" }, "other": "#/components/schemas/Literal" }
            """;

        var rewritten = RefRewriter.RewriteToLocal(input);

        using var doc = JsonDocument.Parse(rewritten);
        Assert.Equal(
            "#/$defs/Node",
            doc.RootElement.GetProperty("items").GetProperty("$ref").GetString()
        );
        // A plain string value that merely contains the prefix must NOT be touched (structural rewrite only).
        Assert.Equal(
            "#/components/schemas/Literal",
            doc.RootElement.GetProperty("other").GetString()
        );
    }

    [Fact]
    public void RewriteToAbsolute_RewritesAgainstBaseUri()
    {
        const string input = """{ "$ref": "#/components/schemas/Node" }""";

        var rewritten = RefRewriter.RewriteToAbsolute(input, "openapi://contract/");

        using var doc = JsonDocument.Parse(rewritten);
        Assert.Equal(
            "openapi://contract/#/$defs/Node",
            doc.RootElement.GetProperty("$ref").GetString()
        );
    }

    [Fact]
    public void RewriteToLocal_HandlesNestedArraysAndNonMatchingRefs()
    {
        const string input = """
            { "allOf": [ { "$ref": "#/components/schemas/A" }, { "$ref": "https://example.com/other" } ] }
            """;

        var rewritten = RefRewriter.RewriteToLocal(input);

        using var doc = JsonDocument.Parse(rewritten);
        var allOf = doc.RootElement.GetProperty("allOf");
        Assert.Equal("#/$defs/A", allOf[0].GetProperty("$ref").GetString());
        Assert.Equal("https://example.com/other", allOf[1].GetProperty("$ref").GetString());
    }

    [Fact]
    public void RewriteToAbsolute_EmptyBaseUri_UsesDefsPrefix()
    {
        const string input = """{ "$ref": "#/components/schemas/Node" }""";

        var rewritten = RefRewriter.RewriteToAbsolute(input, "");

        using var doc = JsonDocument.Parse(rewritten);
        Assert.Equal("#/$defs/Node", doc.RootElement.GetProperty("$ref").GetString());
    }

    [Fact]
    public void RewriteToAbsolute_BaseUriWithoutTrailingSlash_AddsSlash()
    {
        const string input = """{ "$ref": "#/components/schemas/Node" }""";

        var rewritten = RefRewriter.RewriteToAbsolute(input, "https://example.com");

        using var doc = JsonDocument.Parse(rewritten);
        Assert.Equal(
            "https://example.com/#/$defs/Node",
            doc.RootElement.GetProperty("$ref").GetString()
        );
    }
}
