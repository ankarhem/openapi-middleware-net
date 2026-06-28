using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenApiContractValidation.Middleware;
using OpenApiContractValidation.Options;
using Xunit;

namespace OpenApiContractValidation.Tests.Middleware;

/// <summary>
/// Unit tests for <see cref="OpenApiValidationServiceCollectionExtensions"/>. Covers both
/// overloads (with and without a configure delegate) and their null-argument guards, asserting
/// the resulting <see cref="ServiceDescriptor"/> registrations without resolving the singleton
/// (which would throw without a configured contract source).
/// </summary>
public class OpenApiValidationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOpenApiValidation_NoConfig_RegistersValidatorAsSingleton()
    {
        var services = new ServiceCollection();
        var returned = services.AddOpenApiValidation();

        Assert.Same(services, returned);
        Assert.Contains(
            services,
            d =>
                d.ServiceType == typeof(OpenApiContractValidator)
                && d.Lifetime == ServiceLifetime.Singleton
        );
    }

    [Fact]
    public void AddOpenApiValidation_NoConfig_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() => services.AddOpenApiValidation());
    }

    [Fact]
    public void AddOpenApiValidation_WithConfigure_RegistersValidatorAndConfiguresOptions()
    {
        var services = new ServiceCollection();
        var returned = services.AddOpenApiValidation(o => o.ContractText = "{}");

        Assert.Same(services, returned);
        Assert.Contains(
            services,
            d =>
                d.ServiceType == typeof(OpenApiContractValidator)
                && d.Lifetime == ServiceLifetime.Singleton
        );

        // The configure delegate wired IOptions<OpenApiValidationOptions>; verify it ran by
        // resolving only the IOptions (cheap, does not construct the validator).
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenApiValidationOptions>>().Value;
        Assert.Equal("{}", options.ContractText);
    }

    [Fact]
    public void AddOpenApiValidation_WithConfigure_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() => services.AddOpenApiValidation(_ => { }));
    }

    [Fact]
    public void AddOpenApiValidation_WithConfigure_NullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddOpenApiValidation(null!));
    }
}
