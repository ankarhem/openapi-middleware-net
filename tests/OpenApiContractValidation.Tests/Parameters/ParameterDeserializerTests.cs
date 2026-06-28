using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using OpenApiContractValidation.Errors;
using OpenApiContractValidation.Models;
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

    [Fact]
    public void Content_NonJsonMediaType_ReturnsString()
    {
        // A non-JSON media type: the raw value is wrapped as a STRING JsonValue (not parsed).
        var parameter = new OpenApiParameter
        {
            Name = "filter",
            In = ParameterLocation.Query,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["text/plain"] = new OpenApiMediaType(),
            },
        };

        var result = ParameterDeserializer.DeserializeContent(parameter, "[1,2,3]");

        var value = Assert.IsType<JsonValue>(result, exactMatch: false);
        Assert.Equal("[1,2,3]", value.GetValue<string>());
    }

    [Fact]
    public void Content_PlusJsonMediaType_ParsesAsJson()
    {
        // "application/vnd.x+json" is recognized as JSON via the +json suffix.
        var parameter = new OpenApiParameter
        {
            Name = "filter",
            In = ParameterLocation.Query,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/vnd.x+json"] = new OpenApiMediaType(),
            },
        };

        var result = ParameterDeserializer.DeserializeContent(parameter, "{\"a\":1}");

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal(1, obj["a"]!.GetValue<int>());
    }

    [Fact]
    public void Content_NullContent_ReturnsString()
    {
        // No Content map at all: HasJsonMediaType returns false, raw wrapped as STRING.
        var parameter = new OpenApiParameter { Name = "filter", In = ParameterLocation.Query };

        var result = ParameterDeserializer.DeserializeContent(parameter, "raw");

        var value = Assert.IsType<JsonValue>(result, exactMatch: false);
        Assert.Equal("raw", value.GetValue<string>());
    }

    [Fact]
    public void Content_EmptyContent_ReturnsString()
    {
        // An empty Content map: HasJsonMediaType returns false, raw wrapped as STRING.
        var parameter = new OpenApiParameter
        {
            Name = "filter",
            In = ParameterLocation.Query,
            Content = new Dictionary<string, OpenApiMediaType>(),
        };

        var result = ParameterDeserializer.DeserializeContent(parameter, "raw");

        var value = Assert.IsType<JsonValue>(result, exactMatch: false);
        Assert.Equal("raw", value.GetValue<string>());
    }

    [Fact]
    public void Content_NullKey_IsSkipped()
    {
        // A Content map entry with a null key is skipped; the remaining non-JSON key yields a STRING.
        var parameter = new OpenApiParameter
        {
            Name = "filter",
            In = ParameterLocation.Query,
            Content = new NullKeyContent
            {
                { null!, new OpenApiMediaType() },
                { "text/plain", new OpenApiMediaType() },
            },
        };

        var result = ParameterDeserializer.DeserializeContent(parameter, "raw");

        var value = Assert.IsType<JsonValue>(result, exactMatch: false);
        Assert.Equal("raw", value.GetValue<string>());
    }

    [Fact]
    public void Deserialize_RoutesToContentPath_WhenContentPresent()
    {
        // Deserialize() delegates to DeserializeContent when parameter.Content is non-empty.
        var parameter = new OpenApiParameter
        {
            Name = "filter",
            In = ParameterLocation.Query,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["text/plain"] = new OpenApiMediaType(),
            },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "hello" });

        var value = Assert.IsType<JsonValue>(result, exactMatch: false);
        Assert.Equal("hello", value.GetValue<string>());
    }

    [Fact]
    public void Deserialize_EmptyRawValues_ReturnsNull()
    {
        var parameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Query,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };

        var result = ParameterDeserializer.Deserialize(parameter, System.Array.Empty<string>());

        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_UnsupportedStyle_ThrowsStartup()
    {
        // An out-of-range style value hits the switch default arm and throws at startup.
        var parameter = new OpenApiParameter
        {
            Name = "count",
            In = ParameterLocation.Query,
            Style = (ParameterStyle)999,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var ex = Assert.Throws<OpenApiContractValidationException>(() =>
            ParameterDeserializer.Deserialize(parameter, new[] { "42" })
        );
        Assert.Equal(ContractPhase.Startup, ex.Phase);
    }

    [Fact]
    public void Deserialize_DeepObject_ObjectStyle()
    {
        // deepObject via the standard Deserialize() entrypoint: "R=100" style keyed raw values.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = ParameterStyle.DeepObject,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "R=100", "G=200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Deserialize_DeepObject_NonObjectSchema_ReturnsString()
    {
        // deepObject with a non-object schema falls through to ToStringValue.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = ParameterStyle.DeepObject,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "just-a-value" });

        var value = Assert.IsType<JsonValue>(result, exactMatch: false);
        Assert.Equal("just-a-value", value.GetValue<string>());
    }

    [Fact]
    public void DeserializeDeepObject_EmptyDictionary_ReturnsNull()
    {
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = ParameterStyle.DeepObject,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.DeserializeDeepObject(
            parameter,
            new Dictionary<string, string>()
        );

        Assert.Null(result);
    }

    [Fact]
    public void DeserializeDeepObject_NullKey_IsSkipped()
    {
        // A bracketed pair whose key is null is skipped (defensive null-key branch).
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = ParameterStyle.DeepObject,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var pairs = new NullKeyReadOnlyDictionary(
            new[]
            {
                new KeyValuePair<string, string>(null!, "ignored"),
                new KeyValuePair<string, string>("R", "100"),
            }
        );

        var result = ParameterDeserializer.DeserializeDeepObject(parameter, pairs);

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Single(obj);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
    }

    [Fact]
    public void Simple_Object_Explode_KeyedSegments()
    {
        // style=simple, explode=true, object: comma-separated "key=value" keyed segments.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Simple,
            Explode = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "R=100,G=200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Simple_Object_NoExplode_Pairs()
    {
        // style=simple, explode=false, object: alternating key,value pairs.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Simple,
            Explode = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "R,100,G,200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Simple_Object_Explode_SkipsSegmentWithoutEquals()
    {
        // A keyed segment without '=' is ignored by ToObjectFromKeyedSegments.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Simple,
            Explode = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "R=100,bad,G=200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal(2, obj.Count);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Simple_Object_NoExplode_OddTokens_IgnoresLast()
    {
        // 3 tokens -> one pair plus a dangling trailing token that is ignored.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Simple,
            Explode = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "R,100,G" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Single(obj);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
    }

    [Fact]
    public void Form_Object_Explode_KeyedSegments()
    {
        // style=form, explode=true, object: each raw value is a "key=value" segment.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = ParameterStyle.Form,
            Explode = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "R=100", "G=200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Form_Object_NoExplode_Pairs()
    {
        // style=form, explode=false, object: a single comma-delimited key,value sequence.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = ParameterStyle.Form,
            Explode = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "R,100,G,200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void SpaceDelimited_Object_Explode_KeyedSegments()
    {
        // style=spaceDelimited, explode=true, object: keyed segments across raw values.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = ParameterStyle.SpaceDelimited,
            Explode = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "R=100", "G=200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void SpaceDelimited_Object_NoExplode_Pairs()
    {
        // style=spaceDelimited, explode=false, object: space-delimited key,value sequence.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = ParameterStyle.SpaceDelimited,
            Explode = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "R 100 G 200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void PipeDelimited_Object_Explode_KeyedSegments()
    {
        // style=pipeDelimited, explode=true, object: keyed segments across raw values.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = ParameterStyle.PipeDelimited,
            Explode = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "R=100", "G=200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void PipeDelimited_Object_NoExplode_Pairs()
    {
        // style=pipeDelimited, explode=false, object: pipe-delimited key,value sequence.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Query,
            Style = ParameterStyle.PipeDelimited,
            Explode = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "R|100|G|200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Delimited_Primitive_Fallback_ReturnsString()
    {
        // A delimited style with a primitive schema falls through to ToStringValue.
        var parameter = new OpenApiParameter
        {
            Name = "count",
            In = ParameterLocation.Query,
            Style = ParameterStyle.SpaceDelimited,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "42" });

        var value = Assert.IsType<JsonValue>(result, exactMatch: false);
        Assert.Equal("42", value.GetValue<string>());
    }

    [Fact]
    public void Delimited_Array_Explode_MultipleRawValues()
    {
        // Exploded delimited array: each raw value becomes its own element.
        var parameter = QueryArray(ParameterStyle.SpaceDelimited, explode: true);

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "blue", "red" });

        AssertStringArray(result, "blue", "red");
    }

    [Fact]
    public void Matrix_Array_Explode_MultipleSegments()
    {
        // style=matrix, explode=true, array: repeated ";id=" segments become array elements.
        var parameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Matrix,
            Explode = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Array },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { ";id=3;id=4;id=5" });

        AssertStringArray(result, "3", "4", "5");
    }

    [Fact]
    public void Matrix_Object_Explode_KeyedSegments()
    {
        // style=matrix, explode=true (>1 segment), object: keyed segments.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Matrix,
            Explode = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(
            parameter,
            new[] { ";color=R=100;color=G=200" }
        );

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Matrix_Object_NoExplode_Pairs()
    {
        // style=matrix, explode=false, object: comma-delimited pairs after ";name=".
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Matrix,
            Explode = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { ";color=R,100,G,200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Matrix_SegmentWithoutNamePrefix_StripsAtEquals()
    {
        // A matrix segment lacking the "name=" prefix but containing '=': value after '=' is used.
        var parameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Matrix,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { ";other=99" });

        Assert.Equal("99", result!.GetValue<string>());
    }

    [Fact]
    public void Matrix_EmptyRawValue_ReturnsEmptyString()
    {
        // An empty raw value yields a single empty-string segment.
        var parameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Matrix,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "" });

        Assert.Equal("", result!.GetValue<string>());
    }

    [Fact]
    public void Label_Object_Explode_KeyedSegments()
    {
        // style=label, explode=true, object: dot-delimited "key=value" keyed segments.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Label,
            Explode = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { ".R=100.G=200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Label_Object_NoExplode_Pairs()
    {
        // style=label, explode=false, object: comma-delimited pairs after the leading '.'.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Label,
            Explode = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { ".R,100,G,200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Label_Primitive()
    {
        // style=label, primitive: leading '.' stripped, value returned as a STRING.
        var parameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Label,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { ".5" });

        Assert.Equal("5", result!.GetValue<string>());
    }

    [Fact]
    public void Label_Primitive_WithoutLeadingDot()
    {
        // A label value with no leading '.' is returned as-is (the prefix is optional here).
        var parameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Label,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "5" });

        Assert.Equal("5", result!.GetValue<string>());
    }

    [Fact]
    public void ResolveStyle_NullStyle_Query_DefaultsToForm()
    {
        // OpenApiParameter.Style coerces null to Simple, so the Style==null defaulting path is
        // exercised via FakeParameter. In=Query with no Style resolves to Form.
        var parameter = new FakeParameter
        {
            Name = "count",
            In = ParameterLocation.Query,
            Style = null,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "42" });

        Assert.Equal("42", result!.GetValue<string>());
    }

    [Fact]
    public void ResolveStyle_NullStyle_Cookie_DefaultsToForm()
    {
        var parameter = new FakeParameter
        {
            Name = "count",
            In = ParameterLocation.Cookie,
            Style = null,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "42" });

        Assert.Equal("42", result!.GetValue<string>());
    }

    [Fact]
    public void ResolveStyle_NullStyle_Path_DefaultsToSimple()
    {
        var parameter = new FakeParameter
        {
            Name = "count",
            In = ParameterLocation.Path,
            Style = null,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "42" });

        Assert.Equal("42", result!.GetValue<string>());
    }

    [Fact]
    public void ResolveStyle_NullStyle_Header_DefaultsToSimple()
    {
        var parameter = new FakeParameter
        {
            Name = "count",
            In = ParameterLocation.Header,
            Style = null,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "42" });

        Assert.Equal("42", result!.GetValue<string>());
    }

    [Fact]
    public void ResolveStyle_NullStyle_NullIn_ThrowsStartup()
    {
        // Neither Style nor a resolvable In: reported as a startup contract defect.
        var parameter = new FakeParameter
        {
            Name = null!,
            In = null,
            Style = null,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var ex = Assert.Throws<OpenApiContractValidationException>(() =>
            ParameterDeserializer.Deserialize(parameter, new[] { "42" })
        );
        Assert.Equal(ContractPhase.Startup, ex.Phase);
    }

    [Fact]
    public void Content_EmptyRawValues_ReturnsEmptyString()
    {
        // The content path runs before the empty-rawValues guard, so FirstOrEmpty yields "".
        var parameter = new OpenApiParameter
        {
            Name = "filter",
            In = ParameterLocation.Query,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["text/plain"] = new OpenApiMediaType(),
            },
        };

        var result = ParameterDeserializer.Deserialize(parameter, System.Array.Empty<string>());

        Assert.Equal("", result!.GetValue<string>());
    }

    [Fact]
    public void Form_Primitive_NullSchema_ReturnsString()
    {
        // A null schema makes IsArray/IsObject short-circuit to false, hitting the primitive path.
        var parameter = new OpenApiParameter
        {
            Name = "count",
            In = ParameterLocation.Query,
            Style = ParameterStyle.Form,
            Schema = null,
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "42" });

        var value = Assert.IsType<JsonValue>(result, exactMatch: false);
        Assert.Equal("42", value.GetValue<string>());
    }

    [Fact]
    public void Form_Primitive_SchemaWithoutType_ReturnsString()
    {
        // A schema with no Type also fails IsArray/IsObject, hitting the primitive path.
        var parameter = new OpenApiParameter
        {
            Name = "count",
            In = ParameterLocation.Query,
            Style = ParameterStyle.Form,
            Schema = new OpenApiSchema(),
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "42" });

        var value = Assert.IsType<JsonValue>(result, exactMatch: false);
        Assert.Equal("42", value.GetValue<string>());
    }

    [Fact]
    public void Matrix_Object_Explode_SingleSegment_FallsBackToPairs()
    {
        // Explode=true but only one segment: the Count>1 condition is false, so pairs are used.
        var parameter = new OpenApiParameter
        {
            Name = "color",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Matrix,
            Explode = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { ";color=R,100,G,200" });

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("100", obj["R"]!.GetValue<string>());
        Assert.Equal("200", obj["G"]!.GetValue<string>());
    }

    [Fact]
    public void Matrix_NullName_NoEquals_ReturnsSegmentAsIs()
    {
        // With no name, prefix is null; a segment without '=' is kept verbatim.
        var parameter = new OpenApiParameter
        {
            Name = null,
            In = ParameterLocation.Path,
            Style = ParameterStyle.Matrix,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { ";abc" });

        Assert.Equal("abc", result!.GetValue<string>());
    }

    [Fact]
    public void Label_Array_NoExplode_SplitsOnComma()
    {
        // style=label, explode=false, array: leading '.' stripped, then comma-delimited.
        var parameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Label,
            Explode = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Array },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { ".3,4,5" });

        AssertStringArray(result, "3", "4", "5");
    }

    [Fact]
    public void Label_Primitive_EmptyValue_ReturnsEmptyString()
    {
        // An empty label value: the Length>0 check short-circuits, value stays "".
        var parameter = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Style = ParameterStyle.Label,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var result = ParameterDeserializer.Deserialize(parameter, new[] { "" });

        Assert.Equal("", result!.GetValue<string>());
    }

    /// <summary>
    /// Minimal <see cref="IOpenApiParameter"/> whose <see cref="Style"/> and <see cref="In"/>
    /// can genuinely be null (unlike <see cref="OpenApiParameter"/>, which coerces a null
    /// <see cref="Style"/> to <see cref="ParameterStyle.Simple"/>). Only the members accessed
    /// by <see cref="ParameterDeserializer"/> are implemented; the rest throw.
    /// </summary>
    private sealed class FakeParameter : IOpenApiParameter
    {
        public string Name { get; set; } = "";
        public ParameterLocation? In { get; set; }
        public ParameterStyle? Style { get; set; }
        public bool Explode { get; set; }
        public IOpenApiSchema? Schema { get; set; }
        public IDictionary<string, OpenApiMediaType>? Content { get; set; }

        bool IOpenApiParameter.Required => throw new NotSupportedException();
        bool IOpenApiParameter.Deprecated => throw new NotSupportedException();
        bool IOpenApiParameter.AllowEmptyValue => throw new NotSupportedException();
        bool IOpenApiParameter.AllowReserved => throw new NotSupportedException();
        IDictionary<string, IOpenApiExample>? IOpenApiParameter.Examples =>
            throw new NotSupportedException();
        JsonNode? IOpenApiParameter.Example => throw new NotSupportedException();

        string? IOpenApiDescribedElement.Description
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        IDictionary<string, IOpenApiExtension>? IOpenApiReadOnlyExtensible.Extensions =>
            throw new NotSupportedException();

        IOpenApiParameter IShallowCopyable<IOpenApiParameter>.CreateShallowCopy() =>
            throw new NotSupportedException();

        void IOpenApiSerializable.SerializeAsV3(IOpenApiWriter writer) =>
            throw new NotSupportedException();

        void IOpenApiSerializable.SerializeAsV2(IOpenApiWriter writer) =>
            throw new NotSupportedException();

        void IOpenApiSerializable.SerializeAsV31(IOpenApiWriter writer) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// A read-only dictionary whose enumerator yields the supplied pairs as-is, including
    /// null keys (used to exercise defensive null-key branches). Only enumeration is supported.
    /// </summary>
    private sealed class NullKeyReadOnlyDictionary : IReadOnlyDictionary<string, string>
    {
        private readonly List<KeyValuePair<string, string>> _pairs;

        public NullKeyReadOnlyDictionary(IEnumerable<KeyValuePair<string, string>> pairs) =>
            _pairs = pairs.ToList();

        public int Count => _pairs.Count;
        public IEnumerable<string> Keys => _pairs.Select(p => p.Key);
        public IEnumerable<string> Values => _pairs.Select(p => p.Value);
        public string this[string key] => throw new NotSupportedException();

        public bool ContainsKey(string key) => throw new NotSupportedException();

        public bool TryGetValue(string key, out string value) => throw new NotSupportedException();

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _pairs.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            _pairs.GetEnumerator();
    }

    /// <summary>
    /// A dictionary that stores entries in insertion order and exposes a <see cref="Keys"/>
    /// collection including null keys (used to exercise defensive null-key branches).
    /// Only Count, Keys and Add are supported.
    /// </summary>
    private sealed class NullKeyContent : IDictionary<string, OpenApiMediaType>
    {
        private readonly List<KeyValuePair<string, OpenApiMediaType>> _pairs = new();

        public int Count => _pairs.Count;
        public bool IsReadOnly => false;
        public ICollection<string> Keys => _pairs.Select(p => p.Key).ToList();
        public ICollection<OpenApiMediaType> Values => _pairs.Select(p => p.Value).ToList();

        public OpenApiMediaType this[string key]
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void Add(string key, OpenApiMediaType value) =>
            _pairs.Add(new KeyValuePair<string, OpenApiMediaType>(key, value));

        public void Add(KeyValuePair<string, OpenApiMediaType> item) => _pairs.Add(item);

        public void Clear() => _pairs.Clear();

        public bool Contains(KeyValuePair<string, OpenApiMediaType> item) =>
            throw new NotSupportedException();

        public bool ContainsKey(string key) => throw new NotSupportedException();

        public void CopyTo(KeyValuePair<string, OpenApiMediaType>[] array, int arrayIndex) =>
            throw new NotSupportedException();

        public IEnumerator<KeyValuePair<string, OpenApiMediaType>> GetEnumerator() =>
            _pairs.GetEnumerator();

        public bool Remove(string key) => throw new NotSupportedException();

        public bool Remove(KeyValuePair<string, OpenApiMediaType> item) =>
            throw new NotSupportedException();

        public bool TryGetValue(string key, out OpenApiMediaType value) =>
            throw new NotSupportedException();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            _pairs.GetEnumerator();
    }
}
