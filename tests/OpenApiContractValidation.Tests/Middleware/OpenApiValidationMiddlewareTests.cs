using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenApiContractValidation.Errors;
using OpenApiContractValidation.Middleware;
using OpenApiContractValidation.Models;
using OpenApiContractValidation.Options;
using Xunit;

namespace OpenApiContractValidation.Tests.Middleware;

/// <summary>
/// End-to-end integration tests for <see cref="OpenApiContractValidation.Middleware.OpenApiValidationMiddleware"/>.
/// Each test spins up a <see cref="TestServer"/> with a controllable terminal <see cref="RequestDelegate"/>
/// and the OpenAPI validation middleware installed, then asserts that conformant traffic passes while any
/// contract violation (undocumented path/method, body schema drift, undocumented status) surfaces as an
/// <see cref="OpenApiContractValidationException"/> whose response body never reaches the client.
/// </summary>
public class OpenApiValidationMiddlewareTests
{
    /// <summary>
    /// Inline contract: <c>GET /users/{id}</c> -> 200 application/json {id:int required, name:string required};
    /// <c>POST /users</c> -> requestBody application/json required {name:string required}, 201 application/json
    /// {id:int required, name:string required}.
    /// </summary>
    private const string ContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "middleware-tests", "version": "1.0.0" },
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
            },
            "/users": {
              "post": {
                "operationId": "createUser",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object",
                        "required": ["name"],
                        "properties": {
                          "name": { "type": "string" }
                        }
                      }
                    }
                  }
                },
                "responses": {
                  "201": {
                    "description": "created",
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

    /// <summary>
    /// <c>HEAD /items/{id}</c> (integer path parameter) -> 200 with no declared response content. Used to
    /// prove HEAD responses force <c>HasBody=false</c> so a body the terminal writes is never validated.
    /// </summary>
    private const string HeadContractJson = """
        {
          "openapi": "3.1.0",
          "info": { "title": "head-tests", "version": "1.0.0" },
          "paths": {
            "/items/{id}": {
              "head": {
                "operationId": "headItem",
                "parameters": [
                  { "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } }
                ],
                "responses": {
                  "200": { "description": "ok" }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public async Task ConformantRequestAndResponse_Passes()
    {
        var outcome = await InvokeAsync(
            request: () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Get, "/users/1");
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return message;
            },
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"id":1,"name":"x"}""");
            }
        );

        Assert.True(outcome.Exception is null, outcome.Exception?.Message ?? string.Empty);
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
        var body = await outcome.Response.Content.ReadAsStringAsync();
        Assert.Equal("""{"id":1,"name":"x"}""", body);
    }

    [Fact]
    public async Task UndocumentedPath_Throws()
    {
        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/nope"),
            terminal: _ => Task.CompletedTask
        );

        AssertPhase(outcome, ContractPhase.Request);
    }

    [Fact]
    public async Task UndocumentedMethod_Throws()
    {
        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Delete, "/users/1"),
            terminal: _ => Task.CompletedTask
        );

        AssertPhase(outcome, ContractPhase.Request);
    }

    [Fact]
    public async Task ResponseBodySchemaViolation_Throws_AndClientDoesNotGetBadBody()
    {
        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/users/1"),
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"id":"notInt"}""");
            }
        );

        // Response violation must surface and the offending body must NEVER reach the client.
        var ex = AssertPhase(outcome, ContractPhase.Response);

        // If the host rendered a 500 (rather than rethrowing), ensure it did not echo the bad body.
        if (outcome.Response is not null)
        {
            var body = await outcome.Response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("notInt", body);
        }

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task RequestBodyViolation_Throws()
    {
        var outcome = await InvokeAsync(
            request: () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, "/users")
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
                return message;
            },
            terminal: _ => Task.CompletedTask
        );

        AssertPhase(outcome, ContractPhase.Request);
    }

    [Fact]
    public async Task ValidPost_Passes()
    {
        var outcome = await InvokeAsync(
            request: () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, "/users")
                {
                    Content = new StringContent(
                        """{"name":"y"}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
                return message;
            },
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 201;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"id":2,"name":"y"}""");
            }
        );

        Assert.True(outcome.Exception is null, outcome.Exception?.Message ?? string.Empty);
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.Created, outcome.Response!.StatusCode);
        var body = await outcome.Response.Content.ReadAsStringAsync();
        Assert.Equal("""{"id":2,"name":"y"}""", body);
    }

    [Fact]
    public async Task ResponseStatusUndocumented_Throws()
    {
        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/users/1"),
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 418;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"iAm":"teapot"}""");
            }
        );

        AssertPhase(outcome, ContractPhase.Response);
    }

    [Fact]
    public async Task ValidateNone_PassesThrough_UndocumentedPathDoesNotThrow()
    {
        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/completely-undocumented"),
            terminal: ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            },
            direction: ValidationDirection.None
        );

        Assert.True(outcome.Exception is null, outcome.Exception?.Message ?? string.Empty);
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task ValidateRequestOnly_InvalidResponseNotValidated_ClientGetsBadBody()
    {
        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/users/1"),
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"id":"notInt"}""");
            },
            direction: ValidationDirection.Request
        );

        Assert.True(outcome.Exception is null, outcome.Exception?.Message ?? string.Empty);
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
        var body = await outcome.Response.Content.ReadAsStringAsync();
        Assert.Contains("notInt", body);
    }

    [Fact]
    public async Task ValidateResponseOnly_RequestBodyValidationSkipped_BadRequestBodyAccepted()
    {
        var outcome = await InvokeAsync(
            request: () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, "/users")
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
                return message;
            },
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 201;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"id":2,"name":"y"}""");
            },
            direction: ValidationDirection.Response
        );

        Assert.True(outcome.Exception is null, outcome.Exception?.Message ?? string.Empty);
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.Created, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task ResponseDisablesBuffering_Streaming_SkipsValidation_AndPassesThrough()
    {
        // A response that disables buffering (streaming) cannot be captured/validated. The middleware
        // skips validation rather than failing, so the response reaches the client unchanged.
        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/users/1"),
            terminal: ctx =>
            {
                ctx.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync("""{"id":1,"name":"x"}""");
            },
            direction: ValidationDirection.Both
        );

        Assert.Null(outcome.Exception);
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
        Assert.Contains("\"id\":1", await outcome.Response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NonJsonRequestBody_OnRequiredJsonOperation_ThrowsRequestPhase()
    {
        var outcome = await InvokeAsync(
            request: () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, "/users")
                {
                    Content = new StringContent("hi", Encoding.UTF8, "text/plain"),
                };
                return message;
            },
            terminal: _ => Task.CompletedTask,
            direction: ValidationDirection.Both
        );

        AssertPhase(outcome, ContractPhase.Request);
    }

    [Fact]
    public async Task MalformedJsonRequestBody_OnOperationWithoutDeclaredBody_PassesThrough()
    {
        // GET has no declared requestBody, so a present-but-unparseable JSON body yields Body=null but
        // no body validation runs. Exercises the TryParseJson catch on the request side (line ~351).
        var outcome = await InvokeAsync(
            request: () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Get, "/users/1")
                {
                    Content = new StringContent("{bad", Encoding.UTF8, "application/json"),
                };
                return message;
            },
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"id":1,"name":"x"}""");
            },
            direction: ValidationDirection.Both
        );

        Assert.True(outcome.Exception is null, outcome.Exception?.Message ?? string.Empty);
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task MalformedJsonResponseBody_InvalidJsonPassedThroughAsNullBody()
    {
        // TryParseJson catch on the response side yields Body=null; ResponseValidator treats a null body
        // as "no schema instance to evaluate" and returns success, so the bad bytes reach the client.
        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/users/1"),
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{bad");
            },
            direction: ValidationDirection.Both
        );

        Assert.True(outcome.Exception is null, outcome.Exception?.Message ?? string.Empty);
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
        var body = await outcome.Response.Content.ReadAsStringAsync();
        Assert.Equal("{bad", body);
    }

    [Fact]
    public async Task CookieHeader_OnDocumentedGet_PassesThrough()
    {
        var outcome = await InvokeAsync(
            request: () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Get, "/users/1");
                message.Headers.Add("Cookie", "a=b");
                return message;
            },
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"id":1,"name":"x"}""");
            },
            direction: ValidationDirection.Both
        );

        Assert.True(outcome.Exception is null, outcome.Exception?.Message ?? string.Empty);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task MultiValueQueryParameter_OnDocumentedGet_PassesThrough()
    {
        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/users/1?ids=1&ids=2"),
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"id":1,"name":"x"}""");
            },
            direction: ValidationDirection.Both
        );

        Assert.True(outcome.Exception is null, outcome.Exception?.Message ?? string.Empty);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task HeadRequest_WrittenResponseBody_NotValidated()
    {
        // The HEAD operation declares a 200 with no content; if HasBody were true the ResponseValidator
        // would flag "response body returned but none is documented". isHead forces HasBody=false, so the
        // body the terminal writes is ignored and the response passes.
        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Head, "/items/1"),
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync("body-that-must-be-ignored");
            },
            direction: ValidationDirection.Both,
            contract: HeadContractJson
        );

        Assert.True(outcome.Exception is null, outcome.Exception?.Message ?? string.Empty);
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public void Ctor_NullNext_ThrowsArgumentNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new OpenApiValidationMiddleware(
                null!,
                MakeValidator(),
                NullLogger<OpenApiValidationMiddleware>.Instance
            )
        );
        Assert.Equal("next", ex.ParamName);
    }

    [Fact]
    public void Ctor_NullValidator_ThrowsArgumentNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new OpenApiValidationMiddleware(
                _ => Task.CompletedTask,
                null!,
                NullLogger<OpenApiValidationMiddleware>.Instance
            )
        );
        Assert.Equal("validator", ex.ParamName);
    }

    [Fact]
    public void Ctor_NullLogger_ThrowsArgumentNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new OpenApiValidationMiddleware(_ => Task.CompletedTask, MakeValidator(), null!)
        );
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public async Task EmptyJsonRequestBody_OnRequiredBodyOperation_ThrowsRequestPhase()
    {
        // A body-less POST whose Content-Type is set makes MayHaveBody true, reads an empty rawBody and
        // then short-circuits body parsing (!IsNullOrEmpty("") is false). Covers the empty-rawBody branch.
        var outcome = await InvokeAsync(
            request: () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, "/users")
                {
                    Content = new StringContent("", Encoding.UTF8, "application/json"),
                };
                return message;
            },
            terminal: _ => Task.CompletedTask,
            direction: ValidationDirection.Both
        );

        AssertPhase(outcome, ContractPhase.Request);
    }

    [Fact]
    public async Task ResponseBodyWithoutContentType_IsJsonContentTypeNullBranch_Passes()
    {
        // A buffered response body whose ContentType is null drives IsJsonContentType(null) -> false, so
        // the body stays unparsed and the content-type matcher (lenient on null) accepts it: the response
        // passes. The goal is to exercise the null branch of IsJsonContentType on the response path.
        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/users/1"),
            terminal: async ctx =>
            {
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync("""{"id":1,"name":"x"}""");
            },
            direction: ValidationDirection.Both
        );

        Assert.True(outcome.Exception is null, outcome.Exception?.Message ?? string.Empty);
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task LogPolicy_RequestViolation_DoesNotThrow_AndReachesHandler()
    {
        var handlerRan = false;

        var outcome = await InvokeWithOptions(
            request: () =>
                new HttpRequestMessage(HttpMethod.Post, "/users")
                {
                    // Missing required "name": a request violation.
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                },
            terminal: ctx =>
            {
                handlerRan = true;
                ctx.Response.StatusCode = 201;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync("""{"id":1,"name":"x"}""");
            },
            configure: o => o.Handling = ViolationHandling.Log
        );

        Assert.Null(outcome.Exception);
        Assert.True(handlerRan, "log policy must let the request reach the handler");
        Assert.Equal(HttpStatusCode.Created, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task LogPolicy_ResponseViolation_DoesNotThrow_AndDeliversInvalidBody()
    {
        var outcome = await InvokeWithOptions(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/users/1"),
            terminal: ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                // "id" should be an integer: a response schema violation.
                return ctx.Response.WriteAsync("""{"id":"not-an-int","name":"x"}""");
            },
            configure: o => o.Handling = ViolationHandling.Log
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
        Assert.Contains("not-an-int", await outcome.Response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnViolation_Callback_IsInvoked_WithViolationDetails()
    {
        OpenApiContractValidationException? observed = null;

        var outcome = await InvokeWithOptions(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/not/in/spec"),
            terminal: ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            },
            configure: o =>
            {
                o.Handling = ViolationHandling.Log;
                o.OnViolation = ex => observed = ex;
            }
        );

        Assert.Null(outcome.Exception);
        Assert.NotNull(observed);
        Assert.Equal(ContractPhase.Request, observed!.Phase);
        Assert.Contains(observed.Violations, v => v.Location == "path");
    }

    [Fact]
    public async Task OnViolation_Callback_RunsEvenUnderThrowPolicy()
    {
        OpenApiContractValidationException? observed = null;

        var outcome = await InvokeWithOptions(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/not/in/spec"),
            terminal: ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            },
            configure: o =>
            {
                o.Handling = ViolationHandling.Throw;
                o.OnViolation = ex => observed = ex;
            }
        );

        // Throw policy: the middleware threw (TestServer rethrows), and the observer still ran first.
        Assert.NotNull(outcome.Exception);
        Assert.NotNull(observed);
        Assert.Same(outcome.Exception, observed);
    }

    [Fact]
    public async Task StreamingOperation_IsSkipped_AndPassesThroughUnvalidated()
    {
        const string streamingContract = """
            {
              "openapi": "3.1.0",
              "info": { "title": "t", "version": "1" },
              "paths": {
                "/events": {
                  "get": {
                    "operationId": "getEvents",
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

        var outcome = await InvokeAsync(
            request: () => new HttpRequestMessage(HttpMethod.Get, "/events"),
            terminal: ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "text/event-stream";
                // Not JSON; would fail schema validation if the op were not skipped.
                return ctx.Response.WriteAsync("data: hello\n\n");
            },
            direction: ValidationDirection.Both,
            contract: streamingContract
        );

        Assert.Null(outcome.Exception);
        Assert.Equal(HttpStatusCode.OK, outcome.Response!.StatusCode);
        Assert.Contains("data: hello", await outcome.Response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The outcome of a single client invocation: either the validation middleware threw an
    /// <see cref="OpenApiContractValidationException"/> (TestServer rethrows unhandled exceptions by
    /// default) or the host rendered an HTTP response (typically 500 when it swallowed the exception).
    /// </summary>
    private sealed record RequestOutcome(
        OpenApiContractValidationException? Exception,
        HttpResponseMessage? Response
    );

    /// <summary>
    /// Builds a fresh host backed by a <see cref="TestServer"/> with the validation middleware wrapping
    /// the supplied terminal <paramref name="request"/> delegate and returns the outcome of a single
    /// invocation. Uses the modern generic-host + <c>UseTestServer</c> wiring (the legacy
    /// <c>WebHostBuilder</c> is deprecated in ASP.NET Core 10).
    /// </summary>
    private static async Task<RequestOutcome> InvokeAsync(
        Func<HttpRequestMessage> request,
        RequestDelegate terminal
    ) =>
        await InvokeAsyncCore(request, terminal, ValidationDirection.Both, ContractJson)
            .ConfigureAwait(false);

    /// <summary>
    /// Builds a fresh host for a single invocation with an explicit <paramref name="direction"/> and
    /// optional <paramref name="contract"/> (defaults to <see cref="ContractJson"/>).
    /// </summary>
    private static Task<RequestOutcome> InvokeAsync(
        Func<HttpRequestMessage> request,
        RequestDelegate terminal,
        ValidationDirection direction,
        string contract = ContractJson
    ) => InvokeAsyncCore(request, terminal, direction, contract);

    /// <summary>
    /// Builds a standalone <see cref="OpenApiContractValidator"/> from <see cref="ContractJson"/> for the
    /// direct constructor null-argument tests (which do not run the full host).
    /// </summary>
    private static OpenApiContractValidator MakeValidator() =>
        new(
            Microsoft.Extensions.Options.Options.Create(
                new OpenApiValidationOptions
                {
                    ContractText = ContractJson,
                    ContractFormat = "json",
                }
            )
        );

    /// <summary>
    /// Builds a host whose validation options are configured by <paramref name="configure"/>, so tests can
    /// exercise <see cref="ViolationHandling"/> and <see cref="OpenApiValidationOptions.OnViolation"/>.
    /// </summary>
    private static Task<RequestOutcome> InvokeWithOptions(
        Func<HttpRequestMessage> request,
        RequestDelegate terminal,
        Action<OpenApiValidationOptions> configure
    ) => InvokeAsyncCore(request, terminal, ContractJson, configure);

    private static Task<RequestOutcome> InvokeAsyncCore(
        Func<HttpRequestMessage> request,
        RequestDelegate terminal,
        ValidationDirection direction,
        string contract
    ) =>
        InvokeAsyncCore(
            request,
            terminal,
            contract,
            options =>
            {
                options.ContractText = contract;
                options.ContractFormat = "json";
                options.Validate = direction;
            }
        );

    private static async Task<RequestOutcome> InvokeAsyncCore(
        Func<HttpRequestMessage> request,
        RequestDelegate terminal,
        string contract,
        Action<OpenApiValidationOptions> configure
    )
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
                services.AddOpenApiValidation(options =>
                {
                    options.ContractText = contract;
                    options.ContractFormat = "json";
                    configure(options);
                })
            );
            webHost.Configure(app =>
            {
                app.UseOpenApiValidation();
                app.Run(terminal);
            });
        });

        var host = hostBuilder.Build();
        try
        {
            await host.StartAsync();
            using var client = host.GetTestServer().CreateClient();
            using var message = request();

            try
            {
                var response = await client.SendAsync(message);
                return new RequestOutcome(null, response);
            }
            catch (OpenApiContractValidationException ex)
            {
                return new RequestOutcome(ex, null);
            }
        }
        finally
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
    }

    /// <summary>
    /// Asserts that the invocation surfaced a contract violation in the expected phase, accepting either
    /// a rethrown exception or a host-rendered 500.
    /// </summary>
    private static OpenApiContractValidationException? AssertPhase(
        RequestOutcome outcome,
        ContractPhase expectedPhase
    )
    {
        if (outcome.Exception is not null)
        {
            Assert.Equal(expectedPhase, outcome.Exception.Phase);
            Assert.True(outcome.Exception.Violations.Count > 0);
            return outcome.Exception;
        }

        // Host swallowed the exception: it must surface as a 500.
        Assert.NotNull(outcome.Response);
        Assert.Equal(HttpStatusCode.InternalServerError, outcome.Response!.StatusCode);
        return null;
    }
}
