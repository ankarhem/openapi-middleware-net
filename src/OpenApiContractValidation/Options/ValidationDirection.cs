namespace OpenApiContractValidation.Options;

/// <summary>
/// Selects which lifecycle directions are validated against the OpenAPI contract.
/// </summary>
[Flags]
public enum ValidationDirection
{
    /// <summary>No validation.</summary>
    None = 0,

    /// <summary>Validate requests.</summary>
    Request = 1,

    /// <summary>Validate responses.</summary>
    Response = 2,

    /// <summary>Validate both requests and responses.</summary>
    Both = Request | Response,
}
