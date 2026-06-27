using System.Net.Http;
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

namespace OpenApiContractValidation.Tests.E2E;

/// <summary>
/// Shared E2E infrastructure: loads the SampleApi <c>contract.yaml</c> once and provides
/// inline <see cref="TestServer"/> host builders that reuse the exact same contract text so that
/// request and response specs always match the SampleApi. Two host flavours are offered:
/// <list type="bullet">
/// <item><term>Raw</term><description>no exception handler — a contract violation is rethrown to the
/// <see cref="HttpClient"/> caller so tests can assert
/// <see cref="OpenApiContractValidationException.Phase"/> and <c>Violations</c>.</description></item>
/// <item><term>Production</term><description>installs <c>UseExceptionHandler</c> above the validation
/// middleware so a violation surfaces as a clean 500 whose body is the handler's output (proving the
/// offending response body is suppressed and never reaches the client).</description></item>
/// </list>
/// </summary>
internal static class E2EHosts
{
    /// <summary>The full contract text, read from the linked <c>contract.yaml</c> copied to the test output.</summary>
    public static readonly string ContractText = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "contract.yaml")
    );

    /// <summary>
    /// Builds and starts a raw inline <see cref="TestServer"/> host (no exception handler) wrapping
    /// <paramref name="terminal"/> with the validation middleware, sends <paramref name="request"/>,
    /// and returns either the thrown <see cref="OpenApiContractValidationException"/> or the response.
    /// </summary>
    public static async Task<RawOutcome> SendRawAsync(
        RequestDelegate terminal,
        HttpRequestMessage request
    )
    {
        using var host = await BuildHostAsync(terminal, production: false);
        using var client = host.GetTestServer().CreateClient();

        try
        {
            var response = await client.SendAsync(request);
            return new RawOutcome(null, response);
        }
        catch (OpenApiContractValidationException ex)
        {
            return new RawOutcome(ex, null);
        }
    }

    /// <summary>
    /// Builds and starts a production-like inline <see cref="TestServer"/> host that installs an
    /// exception handler above the validation middleware, then sends <paramref name="request"/>. A
    /// response violation therefore surfaces as a 500 whose body is the handler's generic error —
    /// never the offending payload.
    /// </summary>
    public static async Task<HttpResponseMessage> SendProductionAsync(
        RequestDelegate terminal,
        HttpRequestMessage request
    )
    {
        using var host = await BuildHostAsync(terminal, production: true);
        using var client = host.GetTestServer().CreateClient();
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Builds, starts and returns a raw inline <see cref="TestServer"/> host for tests that need
    /// direct access to the client (e.g. multiple requests against one host). The caller disposes
    /// the returned <see cref="IHost"/>.
    /// </summary>
    public static async Task<IHost> StartRawHostAsync(RequestDelegate terminal)
    {
        var host = await BuildHostAsync(terminal, production: false);
        return host;
    }

    private static async Task<IHost> BuildHostAsync(RequestDelegate terminal, bool production)
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
                services.AddOpenApiValidation(options =>
                {
                    options.ContractText = ContractText;
                    options.ContractFormat = "yaml";
                    options.Validate = ValidationDirection.Both;
                })
            );
            webHost.Configure(app =>
            {
                if (production)
                {
                    app.UseExceptionHandler(errApp =>
                        errApp.Run(async ctx =>
                        {
                            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                            ctx.Response.ContentType = "application/json";
                            await ctx.Response.WriteAsync(
                                """{"message":"internal server error"}"""
                            );
                        })
                    );
                }

                app.UseOpenApiValidation();
                app.Run(terminal);
            });
        });

        var host = hostBuilder.Build();
        await host.StartAsync();
        return host;
    }

    /// <summary>The outcome of a raw-host invocation: a thrown exception OR an HTTP response.</summary>
    internal sealed record RawOutcome(
        OpenApiContractValidationException? Exception,
        HttpResponseMessage? Response
    )
    {
        /// <summary>Asserts a violation was thrown in the expected phase and returns the exception.</summary>
        public OpenApiContractValidationException AssertThrown(ContractPhase expected)
        {
            Assert.NotNull(Exception);
            Assert.Equal(expected, Exception!.Phase);
            Assert.True(Exception.Violations.Count > 0, "expected at least one violation");
            return Exception;
        }
    }
}
