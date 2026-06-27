using System.IO;
using System.Text;
using Microsoft.OpenApi;
using OpenApiContractValidation.Errors;
using OpenApiContractValidation.Loader;
using OpenApiContractValidation.Models;
using Xunit;

namespace OpenApiContractValidation.Tests.Loader;

public class OpenApiDocumentLoaderTests
{
    /// <summary>Minimal OpenAPI 3.0.3 document in JSON with one path <c>/pets</c>.</summary>
    private const string Json30 = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Pets API", "version": "1.0.0" },
          "paths": {
            "/pets": {
              "get": {
                "responses": { "200": { "description": "A list of pets." } }
              }
            }
          }
        }
        """;

    /// <summary>Minimal OpenAPI 3.1.0 document in YAML with one path <c>/items</c>.</summary>
    private const string Yaml31 = """
        openapi: 3.1.0
        info:
          title: Items API
          version: 1.0.0
        paths:
          /items:
            get:
              responses:
                '200':
                  description: A list of items.
        """;

    private readonly OpenApiDocumentLoader _loader = new();

    [Fact]
    public void LoadFromText_Json30_ParsesPaths()
    {
        var doc = _loader.LoadFromText(Json30, "json");

        Assert.NotNull(doc.Paths);
        Assert.True(doc.Paths!.ContainsKey("/pets"));
    }

    [Fact]
    public void LoadFromText_Yaml31_ParsesPaths()
    {
        var doc = _loader.LoadFromText(Yaml31, "yaml");

        Assert.NotNull(doc.Paths);
        Assert.True(doc.Paths!.ContainsKey("/items"));
    }

    [Fact]
    public void LoadFromFile_Yaml_Works()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"oapi-loader-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(tempFile, Yaml31, Encoding.UTF8);

            var doc = _loader.LoadFromFile(tempFile);

            Assert.NotNull(doc.Paths);
            Assert.True(doc.Paths!.ContainsKey("/items"));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void LoadFromStream_Json_Works()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json30));

        var doc = _loader.LoadFromStream(stream, "json");

        Assert.NotNull(doc.Paths);
        Assert.True(doc.Paths!.ContainsKey("/pets"));
    }

    [Fact]
    public void Malformed_ThrowsStartup()
    {
        var ex = Assert.Throws<OpenApiContractValidationException>(() =>
            _loader.LoadFromText("{ this is not valid openapi", "json")
        );

        Assert.Equal(ContractPhase.Startup, ex.Phase);
        Assert.NotEmpty(ex.Violations);
    }

    [Fact]
    public void EmptyOrNonOpenApi_ThrowsStartup()
    {
        var ex = Assert.Throws<OpenApiContractValidationException>(() =>
            _loader.LoadFromText("{}", "json")
        );

        Assert.Equal(ContractPhase.Startup, ex.Phase);
        Assert.NotEmpty(ex.Violations);
    }
}
