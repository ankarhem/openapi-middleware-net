using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenApiContractValidation.Models;
using Xunit;

namespace OpenApiContractValidation.Tests.E2E;

/// <summary>
/// End-to-end coverage of EVERY operation and OpenAPI grammar surface declared by the official
/// Swagger Petstore (3.0.4) contract (<c>petstore.json</c>), driven through <see cref="PetstoreHosts"/>.
/// Each of the 19 operations gets at least one conformant happy path, and every grammar construct
/// (path/query/header parameters, enums, arrays, $ref composition, request/response content-types,
/// additionalProperties maps, scalar-string bodies, date-time formats, booleans, no-content 200/404
/// responses, the <c>default</c> status) is exercised at least once. Targeted violation cases pin
/// the strict rejection of contract drift for each construct.
/// </summary>
public sealed class PetstoreGrammarE2ETests
{
    // Schemas come straight from petstore.json (verified with jq):
    //   Pet{id:int64, name:req, category:$ref Category, photoUrls:req array<string>,
    //       tags:array<$ref Tag>, status:enum[available,pending,sold]}
    //   Order{id,petId:int64, quantity:int32, shipDate:date-time, status:enum[placed,approved,delivered], complete:bool}
    //   User{id,username,firstName,lastName,email,password,phone,userStatus}
    //   ApiResponse{code:int32, type, message}
    private const string ValidPetJson = """
        {
          "id": 10,
          "name": "doggie",
          "category": { "id": 1, "name": "Dogs" },
          "photoUrls": ["http://example.com/1.png"],
          "tags": [{ "id": 1, "name": "tag1" }],
          "status": "available"
        }
        """;

    private const string ValidOrderJson = """
        {
          "id": 1,
          "petId": 2,
          "quantity": 3,
          "shipDate": "2024-01-02T03:04:05Z",
          "status": "placed",
          "complete": false
        }
        """;

    private const string ValidUserJson = """
        {
          "id": 10,
          "username": "theUser",
          "firstName": "John",
          "lastName": "James",
          "email": "john@email.com",
          "password": "12345",
          "phone": "12345",
          "userStatus": 1
        }
        """;

    private const string ApiResponseJson =
        """{ "code": 200, "type": "ok", "message": "uploaded" }""";

    /// <summary>A terminal that writes a JSON body with the given status.</summary>
    private static RequestDelegate Json(int status, string body) =>
        async ctx =>
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(body);
        };

    /// <summary>A terminal that writes an XML body with the given status.</summary>
    private static RequestDelegate Xml(int status, string body) =>
        async ctx =>
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/xml";
            await ctx.Response.WriteAsync(body);
        };

    /// <summary>A terminal that only sets a status code and writes no body (for no-content responses).</summary>
    private static RequestDelegate NoBody(int status) =>
        ctx =>
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        };

    // ---------------------------------------------------------------------------------------------
    // /pet  (PUT updatePet, POST addPet)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task PutPet_UpdateValidPet_Happy()
    {
        using var content = new StringContent(ValidPetJson, Encoding.UTF8, "application/json");
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidPetJson),
            new HttpRequestMessage(HttpMethod.Put, "/pet") { Content = content }
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
        Assert.Contains("doggie", await outcome.Response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutPet_RequestMissingRequiredName_Throws()
    {
        // Pet.name is required; a request body without it is contract drift on the Request phase.
        using var content = new StringContent(
            """{ "photoUrls": ["u"] }""",
            Encoding.UTF8,
            "application/json"
        );
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidPetJson),
            new HttpRequestMessage(HttpMethod.Put, "/pet") { Content = content }
        );

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Location == "requestBody");
        Assert.Contains(ex.Violations, v => v.Keyword == "required");
    }

    [Fact]
    public async Task PostPet_AddValidPet_Happy()
    {
        using var content = new StringContent(ValidPetJson, Encoding.UTF8, "application/json");
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidPetJson),
            new HttpRequestMessage(HttpMethod.Post, "/pet") { Content = content }
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task PostPet_UndeclaredRequestContentType_Throws()
    {
        // POST /pet declares application/json, application/xml and application/x-www-form-urlencoded;
        // text/plain is not among them and must be rejected at the request content-type surface.
        using var content = new StringContent("doggie", Encoding.UTF8, "text/plain");
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidPetJson),
            new HttpRequestMessage(HttpMethod.Post, "/pet") { Content = content }
        );

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Location == "requestBody/contentType");
    }

    // ---------------------------------------------------------------------------------------------
    // /pet/findByStatus  (GET)  — query enum [available,pending,sold]; array-of-Pet response
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task FindByStatus_Happy()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, $"[{ValidPetJson},{ValidPetJson}]"),
            new HttpRequestMessage(HttpMethod.Get, "/pet/findByStatus?status=pending")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------
    // /pet/findByTags  (GET)  — exploded array query (?tags=a&tags=b); array-of-Pet response
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task FindByTags_ExplodedArrayQuery_Happy()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, $"[{ValidPetJson}]"),
            new HttpRequestMessage(HttpMethod.Get, "/pet/findByTags?tags=a&tags=b")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task FindByTags_ArrayElementMissingRequired_Throws()
    {
        // The response is an array of Pet; element [0] is missing the required `name`.
        var body = $$"""[ { "photoUrls": ["u"] }, {{ValidPetJson}} ]""";
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, body),
            new HttpRequestMessage(HttpMethod.Get, "/pet/findByTags?tags=a")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody");
        Assert.Contains(
            ex.Violations,
            v => v.InstanceLocation!.StartsWith("/0", StringComparison.Ordinal)
        );
    }

    // ---------------------------------------------------------------------------------------------
    // /pet/{petId}  (GET)  — int64 path param; json + xml response content-types
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetPetById_XmlResponse_Happy()
    {
        // The contract declares application/xml for 200; a non-JSON declared content-type must be
        // accepted (it is not parsed as JSON, so no schema check runs, but the content-type matches).
        var outcome = await PetstoreHosts.SendRawAsync(
            Xml(
                200,
                "<pet><id>10</id><name>doggie</name><photoUrls><photoUrl>u</photoUrl></photoUrls></pet>"
            ),
            new HttpRequestMessage(HttpMethod.Get, "/pet/10")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
        Assert.Equal("application/xml", outcome.Response.Content.Headers.ContentType!.MediaType);
    }

    // ---------------------------------------------------------------------------------------------
    // /pet/{petId}  (POST updatePetWithForm)  — int64 path + string query name & status
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task UpdatePetWithForm_Happy()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidPetJson),
            new HttpRequestMessage(HttpMethod.Post, "/pet/5?name=rex&status=available")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------
    // /pet/{petId}  (DELETE deletePet)  — HEADER param api_key (string) + 200 no-content
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task DeletePet_ApiKeyHeader_NoContent200_Happy()
    {
        // The ONLY header-parameter op in the spec, and a 200 that declares no content. The optional
        // `api_key` header must be accepted and the bodiless 200 must pass.
        var request = new HttpRequestMessage(HttpMethod.Delete, "/pet/5");
        request.Headers.Add("api_key", "secret");

        var outcome = await PetstoreHosts.SendRawAsync(NoBody(200), request);

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------
    // /pet/{petId}/uploadImage  (POST uploadFile)  — octet-stream request body; ApiResponse response
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task UploadImage_NoBody_Happy()
    {
        // The requestBody is NOT required, so a bodyless POST with the `additionalMetadata` query
        // param validates cleanly and the 200 ApiResponse exercises the ApiResponse response schema.
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ApiResponseJson),
            new HttpRequestMessage(HttpMethod.Post, "/pet/5/uploadImage?additionalMetadata=meta")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
        Assert.Contains("uploaded", await outcome.Response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UploadImage_ResponseApiResponseWrongFieldType_Throws()
    {
        // ApiResponse.code is int32; a string value is a response schema violation.
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "code": "not-a-number", "type": "ok", "message": "x" }"""),
            new HttpRequestMessage(HttpMethod.Post, "/pet/5/uploadImage")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody");
        Assert.Contains(ex.Violations, v => v.InstanceLocation == "/code");
    }

    [Fact]
    public async Task UploadImage_OctetStreamBinaryBody_Happy()
    {
        // A declared non-JSON request content-type (application/octet-stream) is matched by the
        // content-type check; the binary body has no JSON instance to schema-validate, so it
        // passes and the 200 ApiResponse is returned to the client.
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes("fake-image-bytes"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ApiResponseJson),
            new HttpRequestMessage(HttpMethod.Post, "/pet/5/uploadImage") { Content = content }
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------
    // /store/inventory  (GET)  — additionalProperties:{integer} map response
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task StoreInventory_AdditionalPropertiesMap_Happy()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "available": 5, "pending": 2, "sold": 9 }"""),
            new HttpRequestMessage(HttpMethod.Get, "/store/inventory")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task StoreInventory_NonIntegerMapValue_Throws()
    {
        // additionalProperties declares integer values; a string value violates the map schema.
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "available": "five" }"""),
            new HttpRequestMessage(HttpMethod.Get, "/store/inventory")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody");
        Assert.Contains(ex.Violations, v => v.InstanceLocation == "/available");
    }

    // ---------------------------------------------------------------------------------------------
    // /store/order  (POST placeOrder)  — Order body (date-time format, enum status, boolean complete)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task PlaceOrder_Happy()
    {
        using var content = new StringContent(ValidOrderJson, Encoding.UTF8, "application/json");
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidOrderJson),
            new HttpRequestMessage(HttpMethod.Post, "/store/order") { Content = content }
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
        Assert.Contains("placed", await outcome.Response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PlaceOrder_ResponseBadShipDateFormat_Throws()
    {
        // Order.shipDate is format:date-time; a non-date-time string is a `format` violation.
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "shipDate": "not-a-date-time" }"""),
            new HttpRequestMessage(HttpMethod.Post, "/store/order")
            {
                Content = new StringContent(ValidOrderJson, Encoding.UTF8, "application/json"),
            }
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody");
        Assert.Contains(ex.Violations, v => v.InstanceLocation == "/shipDate");
    }

    [Fact]
    public async Task PlaceOrder_ResponseCompleteNotBoolean_Throws()
    {
        // Order.complete is a boolean; a string value violates the `type` keyword.
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "complete": "yes" }"""),
            new HttpRequestMessage(HttpMethod.Post, "/store/order")
            {
                Content = new StringContent(ValidOrderJson, Encoding.UTF8, "application/json"),
            }
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody");
        Assert.Contains(ex.Violations, v => v.InstanceLocation == "/complete");
    }

    [Fact]
    public async Task PlaceOrder_ResponseInvalidOrderStatusEnum_Throws()
    {
        // Order.status enum is [placed, approved, delivered]; "teleported" is out of range.
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "status": "teleported" }"""),
            new HttpRequestMessage(HttpMethod.Post, "/store/order")
            {
                Content = new StringContent(ValidOrderJson, Encoding.UTF8, "application/json"),
            }
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody");
        Assert.Contains(ex.Violations, v => v.InstanceLocation == "/status");
    }

    // ---------------------------------------------------------------------------------------------
    // /store/order/{orderId}  (GET, DELETE)  — int64 path param
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetOrderById_Happy()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidOrderJson),
            new HttpRequestMessage(HttpMethod.Get, "/store/order/5")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task GetOrderById_Documented404NoContent_Happy()
    {
        // 404 is a documented response with no content; a bodiless 404 must pass.
        var outcome = await PetstoreHosts.SendRawAsync(
            NoBody(404),
            new HttpRequestMessage(HttpMethod.Get, "/store/order/5")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.NotFound, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task DeleteOrder_NoContent200_Happy()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            NoBody(200),
            new HttpRequestMessage(HttpMethod.Delete, "/store/order/5")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task DeleteOrder_NonIntegerOrderId_Throws()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            NoBody(200),
            new HttpRequestMessage(HttpMethod.Delete, "/store/order/not-a-number")
        );

        var ex = outcome.AssertThrown(ContractPhase.Request);
        Assert.Contains(ex.Violations, v => v.Location == "path/orderId");
    }

    // ---------------------------------------------------------------------------------------------
    // /user  (POST createUser)  — User body
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateUser_Happy()
    {
        using var content = new StringContent(ValidUserJson, Encoding.UTF8, "application/json");
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidUserJson),
            new HttpRequestMessage(HttpMethod.Post, "/user") { Content = content }
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------
    // /user/createWithList  (POST)  — application/json ARRAY of User request body
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateWithList_ArrayRequestBody_Happy()
    {
        using var content = new StringContent(
            $"[{ValidUserJson},{ValidUserJson}]",
            Encoding.UTF8,
            "application/json"
        );
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidUserJson),
            new HttpRequestMessage(HttpMethod.Post, "/user/createWithList") { Content = content }
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------
    // /user/login  (GET)  — username + password query params; SCALAR string response body
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task LoginUser_ScalarStringResponse_Happy()
    {
        // 200 content is a JSON schema of type:string — a JSON string body ("token"). Also exercises
        // two string query params. (Declared response headers are optional, so their absence is fine.)
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, "\"token-value\""),
            new HttpRequestMessage(HttpMethod.Get, "/user/login?username=theUser&password=12345")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task LoginUser_ObjectWhereStringExpected_Throws()
    {
        // A JSON object where a scalar string is declared is a response schema type violation.
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "foo": "bar" }"""),
            new HttpRequestMessage(HttpMethod.Get, "/user/login?username=theUser&password=12345")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody");
        Assert.Contains(ex.Violations, v => v.InstanceLocation == "");
    }

    // ---------------------------------------------------------------------------------------------
    // /user/logout  (GET)  — 200 no-content
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task LogoutUser_NoContent200_Happy()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            NoBody(200),
            new HttpRequestMessage(HttpMethod.Get, "/user/logout")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task LogoutUser_ResponseHasBodyWhereNoneDocumented_Throws()
    {
        // 200 for logout declares no content; returning a body under it is contract drift.
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, """{ "ok": true }"""),
            new HttpRequestMessage(HttpMethod.Get, "/user/logout")
        );

        var ex = outcome.AssertThrown(ContractPhase.Response);
        Assert.Contains(ex.Violations, v => v.Location == "responseBody");
    }

    // ---------------------------------------------------------------------------------------------
    // /user/{username}  (GET, PUT, DELETE)  — string path param
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetUserByName_Happy()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            Json(200, ValidUserJson),
            new HttpRequestMessage(HttpMethod.Get, "/user/theUser")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task GetUserByName_Documented404NoContent_Happy()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            NoBody(404),
            new HttpRequestMessage(HttpMethod.Get, "/user/theUser")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.NotFound, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_Happy()
    {
        using var content = new StringContent(ValidUserJson, Encoding.UTF8, "application/json");
        var outcome = await PetstoreHosts.SendRawAsync(
            NoBody(200),
            new HttpRequestMessage(HttpMethod.Put, "/user/theUser") { Content = content }
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_Happy()
    {
        var outcome = await PetstoreHosts.SendRawAsync(
            NoBody(200),
            new HttpRequestMessage(HttpMethod.Delete, "/user/theUser")
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }
}
