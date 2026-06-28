namespace OpenApiContractValidation.Options;

/// <summary>
/// Selects what the middleware does when a request or response violates the OpenAPI contract.
/// </summary>
public enum ViolationHandling
{
    /// <summary>
    /// Throw <see cref="Errors.OpenApiContractValidationException"/> on a violation. Invalid responses
    /// are suppressed (never reach the client) because the exception is thrown before the buffered body
    /// is flushed. This is the default.
    /// </summary>
    Throw = 0,

    /// <summary>
    /// Log the violation and continue: the request still reaches the handler, and an invalid response is
    /// still delivered to the client. Useful for observing drift in production without failing requests.
    /// The <see cref="OpenApiValidationOptions.OnViolation"/> callback (when set) still runs.
    /// </summary>
    Log = 1,
}
