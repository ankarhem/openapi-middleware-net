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
}
