using Microsoft.Extensions.DependencyInjection;
using OpenApiContractValidation.Options;

namespace OpenApiContractValidation.Middleware;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> that register the OpenAPI contract validation
/// services.
/// </summary>
public static class OpenApiValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="OpenApiValidationOptions"/> (configured by <paramref name="configure"/>) and
    /// the singleton <see cref="OpenApiContractValidator"/> that loads the contract once at startup.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">A delegate that configures the <see cref="OpenApiValidationOptions"/>.</param>
    /// <returns>The <paramref name="services"/> collection, for chaining.</returns>
    public static IServiceCollection AddOpenApiValidation(
        this IServiceCollection services,
        Action<OpenApiValidationOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddSingleton<OpenApiContractValidator>();
        return services;
    }

    /// <summary>
    /// Registers the singleton <see cref="OpenApiContractValidator"/> for cases where
    /// <see cref="OpenApiValidationOptions"/> is configured elsewhere (for example via
    /// <c>IConfiguration</c> binding). Use <see cref="AddOpenApiValidation(IServiceCollection, Action{OpenApiValidationOptions})"/>
    /// when you want to configure options inline.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The <paramref name="services"/> collection, for chaining.</returns>
    public static IServiceCollection AddOpenApiValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<OpenApiContractValidator>();
        return services;
    }
}
