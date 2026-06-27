namespace OpenApiContractValidation.Models;

/// <summary>
/// The lifecycle phase during which an OpenAPI contract is being validated.
/// </summary>
public enum ContractPhase
{
    /// <summary>The contract is being loaded and validated at application startup.</summary>
    Startup,

    /// <summary>An incoming HTTP request is being validated.</summary>
    Request,

    /// <summary>An outgoing HTTP response is being validated.</summary>
    Response,
}
