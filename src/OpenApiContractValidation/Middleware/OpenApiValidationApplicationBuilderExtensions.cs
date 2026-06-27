using Microsoft.AspNetCore.Builder;

namespace OpenApiContractValidation.Middleware;

/// <summary>
/// Extension methods on <see cref="IApplicationBuilder"/> that add the OpenAPI contract validation
/// middleware to the pipeline.
/// </summary>
public static class OpenApiValidationApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the <see cref="OpenApiValidationMiddleware"/> to the application's request pipeline. The
    /// middleware validates requests and responses against the configured OpenAPI contract and throws
    /// <see cref="Errors.OpenApiContractValidationException"/> on any violation.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The <paramref name="app"/> builder, for chaining.</returns>
    public static IApplicationBuilder UseOpenApiValidation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<OpenApiValidationMiddleware>();
    }
}
