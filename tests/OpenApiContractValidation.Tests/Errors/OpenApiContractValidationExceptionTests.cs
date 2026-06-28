using OpenApiContractValidation.Errors;
using OpenApiContractValidation.Models;
using Xunit;

namespace OpenApiContractValidation.Tests.Errors;

public class OpenApiContractValidationExceptionTests
{
    [Fact]
    public void Constructor_SetsPhaseAndViolations()
    {
        var violation = new ContractViolation(
            "query/id",
            "/id",
            "type",
            "integer",
            "string",
            "value must be integer"
        );

        var ex = new OpenApiContractValidationException(
            ContractPhase.Request,
            "GET",
            "/users/1",
            new[] { violation }
        );

        Assert.Equal(ContractPhase.Request, ex.Phase);
        Assert.Single(ex.Violations);
        Assert.Contains("query/id", ex.Message);
        Assert.Contains("value must be integer", ex.Message);
    }

    [Fact]
    public void ValidationResult_Failure_IsNotValid()
    {
        var violation = new ContractViolation(
            "query/id",
            "/id",
            "type",
            "integer",
            "string",
            "value must be integer"
        );

        Assert.False(ValidationResult.Failure(violation).IsValid);
        Assert.True(ValidationResult.Success.IsValid);
    }

    [Fact]
    public void ContractViolation_AllPropertiesAreAccessible()
    {
        var violation = new ContractViolation(
            Location: "responseBody",
            InstanceLocation: "/data/0/id",
            Keyword: "type",
            Expected: "integer",
            Actual: "string",
            Message: "expected integer"
        );

        Assert.Equal("responseBody", violation.Location);
        Assert.Equal("/data/0/id", violation.InstanceLocation);
        Assert.Equal("type", violation.Keyword);
        Assert.Equal("integer", violation.Expected);
        Assert.Equal("string", violation.Actual);
        Assert.Equal("expected integer", violation.Message);
    }

    [Fact]
    public void Exception_Getters_ReturnMethodAndPath()
    {
        var violation = new ContractViolation(
            "query/id",
            "/id",
            "type",
            "integer",
            "string",
            "value must be integer"
        );

        var ex = new OpenApiContractValidationException(
            ContractPhase.Request,
            "GET",
            "/users/1",
            new[] { violation }
        );

        Assert.Equal(ContractPhase.Request, ex.Phase);
        Assert.Equal("GET", ex.HttpMethod);
        Assert.Equal("/users/1", ex.Path);
    }

    [Fact]
    public void Exception_TwoParamConstructor_SetsNullMethodAndPath()
    {
        var violation = new ContractViolation(
            "status",
            null,
            null,
            null,
            null,
            "undocumented status"
        );

        var ex = new OpenApiContractValidationException(ContractPhase.Startup, new[] { violation });

        Assert.Equal(ContractPhase.Startup, ex.Phase);
        Assert.Null(ex.HttpMethod);
        Assert.Null(ex.Path);
        Assert.Single(ex.Violations);
    }
}
