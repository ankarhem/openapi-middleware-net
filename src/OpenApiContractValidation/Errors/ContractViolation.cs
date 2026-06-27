namespace OpenApiContractValidation.Errors;

/// <summary>
/// Describes a single contract violation.
/// </summary>
/// <param name="Location">Human label such as "query/userId", "responseBody" or "status".</param>
/// <param name="InstanceLocation">JSON Pointer to the offending instance location, if applicable.</param>
/// <param name="Keyword">The validation keyword that failed (e.g. "type", "required"), if applicable.</param>
/// <param name="Expected">The value expected by the contract, if applicable.</param>
/// <param name="Actual">The value observed at runtime, if applicable.</param>
/// <param name="Message">Human-readable description of the violation.</param>
public sealed record ContractViolation(
    string Location,
    string? InstanceLocation,
    string? Keyword,
    string? Expected,
    string? Actual,
    string Message
);
