using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    )
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
                services.AddOpenApiValidation(options =>
                {
                    options.ContractText = ContractJson;
                    options.ContractFormat = "json";
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
