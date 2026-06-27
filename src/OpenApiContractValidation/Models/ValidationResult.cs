using OpenApiContractValidation.Errors;

namespace OpenApiContractValidation.Models;

/// <summary>
/// The outcome of validating against an OpenAPI contract.
/// </summary>
public sealed record ValidationResult
{
    private static readonly IReadOnlyList<ContractViolation> EmptyViolations =
        Array.Empty<ContractViolation>();

    private ValidationResult(bool isValid, IReadOnlyList<ContractViolation> violations)
    {
        IsValid = isValid;
        Violations = violations;
    }

    /// <summary>
    /// <see langword="true"/> when no contract violations were detected.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// The violations found, if any.
    /// </summary>
    public IReadOnlyList<ContractViolation> Violations { get; }

    /// <summary>
    /// A successful validation result with no violations.
    /// </summary>
    public static ValidationResult Success { get; } = new(isValid: true, EmptyViolations);

    /// <summary>
    /// A failed validation result carrying the supplied <paramref name="violations"/>.
    /// </summary>
    public static ValidationResult Failure(IReadOnlyList<ContractViolation> violations) =>
        new(isValid: false, violations);

    /// <summary>
    /// A failed validation result carrying the supplied <paramref name="violations"/>.
    /// </summary>
    public static ValidationResult Failure(params ContractViolation[] violations) =>
        new(isValid: false, violations);
}
