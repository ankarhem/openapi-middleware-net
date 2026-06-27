using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using OpenApiContractValidation.Parameters;
using Xunit;

namespace OpenApiContractValidation.Tests.Parameters;

public class ParameterDeserializerTests
{
    private static OpenApiParameter QueryArray(ParameterStyle style, bool? explode = null) =>
        new()
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = style,
            Explode = explode ?? style == ParameterStyle.Form,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Array },
        };

    /// <summary>
    /// Asserts that a <see cref="JsonNode"/> is a <see cref="JsonArray"/> whose
    /// elements are the supplied expected strings, in order.
    /// </summary>
    private static void AssertStringArray(JsonNode? node, params string[] expected)
    {
        var array = Assert.IsType<JsonArray>(node);
        Assert.Equal(expected.Length, array.Count);

        var actual = array.Select(e => e!.GetValue<string>()).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Form_Explode_Array()
    {
        // style=form, explode=true: each raw value is its own array element.
        var parameter = QueryArray(ParameterStyle.Form, explode: true);

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "blue", "red" });

        AssertStringArray(result, "blue", "red");
    }

    [Fact]
    public void Form_NoExplode_Array()
    {
        // style=form, explode=false: a single comma-delimited string is split.
        var parameter = QueryArray(ParameterStyle.Form, explode: false);

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "blue,red" });

        AssertStringArray(result, "blue", "red");
    }

    [Fact]
    public void Simple_Array()
    {
        // style=simple (path default): comma-delimited.
        var parameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Simple,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Array },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "3,4,5" });

        AssertStringArray(result, "3", "4", "5");
    }

    [Fact]
    public void SpaceDelimited_Array()
    {
        // style=spaceDelimited, explode=false: split on space.
        var parameter = QueryArray(ParameterStyle.SpaceDelimited, explode: false);

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "3 4 5" });

        AssertStringArray(result, "3", "4", "5");
    }

    [Fact]
    public void PipeDelimited_Array()
    {
        // style=pipeDelimited, explode=false: split on pipe.
        var parameter = QueryArray(ParameterStyle.PipeDelimited, explode: false);

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "3|4|5" });

        AssertStringArray(result, "3", "4", "5");
    }

    [Fact]
    public void DeepObject_Object()
    {
        // deepObject: bracketed key/value pairs arrive as a dictionary.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = ParameterStyle.DeepObject,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var pairs = new Dictionary<string, string> { ["R"] = "100", ["G"] = "200" };

        var result = ParameterDeserializer.DeserializeDeepObject(parameter, pairs);

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Matrix_PrimitiveAndArray()
    {
        // style=matrix: leading ";name=" is stripped, then simple/csv semantics apply.
        var arrayParameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Matrix,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Array },
        };
        var primitiveParameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Matrix,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var primitiveResult = ParameterDeserializer.Deserialize(
            primitiveParameter,
            new[] { ";id=5" }
        );
        var arrayResult = ParameterDeserializer.Deserialize(arrayParameter, new[] { ";id=3,4,5" });

        Assert.Equal("5", primitiveResult!.GetValue<string>());
        AssertStringArray(arrayResult, "3", "4", "5");
    }

    [Fact]
    public void Label_Array()
    {
        // style=label, explode=true: leading "." then dot-delimited elements.
        var parameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Label,
            Explode = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Array },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { ".3.4.5" });

        AssertStringArray(result, "3", "4", "5");
    }

    [Fact]
    public void ContentJson()
    {
        // Content-based parameter: the raw value is a JSON document.
        var parameter = new OpenApiParameter
        {
            Name = "filter",
            In = ParameterLocation.Query,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType(),
            },
        };

        var result = ParameterDeserializer.DeserializeContent(parameter, "{\"a\":1}");

        var obj = Assert.IsType<JsonObject>(result);
        // Content parsing yields native JSON types, so "a" is a real number.
        Assert.Equal(1, obj["a"]!.GetValue<int>());
    }

    [Fact]
    public void Primitive_Form()
    {
        // Primitive scalar: token kept as a STRING (no numeric coercion).
        var parameter = new OpenApiParameter
        {
            Name = "count",
            In = ParameterLocation.Query,
            Style = ParameterStyle.Form,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "42" });

        var value = Assert.IsType<JsonValue>(result, exactMatch: false);
        Assert.Equal("42", value.GetValue<string>());
    }
}
