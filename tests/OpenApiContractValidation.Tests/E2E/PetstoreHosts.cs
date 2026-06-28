using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenApiContractValidation.Errors;
using OpenApiContractValidation.Middleware;
using OpenApiContractValidation.Options;

namespace OpenApiContractValidation.Tests.E2E;

/// <summary>
/// E2E infrastructure that validates against the official Swagger Petstore (OpenAPI 3.0.4)
/// JSON contract, copied to the test output as <c>petstore.json</c>. A single inline
/// <see cref="TestServer"/> wraps a controllable terminal endpoint with the validation
/// middleware configured from the real spec, so tests can prove the library works against a
/// well-known, production-grade contract.
/// </summary>
internal static class PetstoreHosts
{
    public static readonly string ContractText = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "petstore.json")
    );

    public static async Task<E2EHosts.RawOutcome> SendRawAsync(
        RequestDelegate terminal,
        HttpRequestMessage request
    )
    {
        using var host = await BuildHostAsync(terminal, production: false);
        using var client = host.GetTestServer().CreateClient();

        try
        {
            var response = await client.SendAsync(request);
            return new E2EHosts.RawOutcome(null, response);
        }
        catch (OpenApiContractValidationException ex)
        {
            return new E2EHosts.RawOutcome(ex, null);
        }
    }

    public static async Task<HttpResponseMessage> SendProductionAsync(
        RequestDelegate terminal,
        HttpRequestMessage request
    )
    {
        using var host = await BuildHostAsync(terminal, production: true);
        using var client = host.GetTestServer().CreateClient();
        return await client.SendAsync(request);
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
                    options.ContractFormat = "json";
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
}
