using System.IO;

namespace OpenApiContractValidation.Options;

/// <summary>
/// Configures how and where the OpenAPI contract is sourced and which directions are validated.
/// </summary>
public sealed class OpenApiValidationOptions
{
    /// <summary>File system path to the OpenAPI contract, when sourcing from disk.</summary>
    public string? ContractFilePath { get; set; }

    /// <summary>A stream providing the OpenAPI contract, when sourcing from a stream.</summary>
    public Stream? ContractStream { get; set; }

    /// <summary>The raw OpenAPI contract text, when sourcing inline.</summary>
    public string? ContractText { get; set; }

    /// <summary>A hint ("json" or "yaml") describing the contract format, when known.</summary>
    public string? ContractFormat { get; set; }

    /// <summary>
    /// The maximum number of bytes buffered while capturing a response body for validation.
    /// Defaults to 10 MiB.
    /// </summary>
    public long MaxResponseBufferSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Which directions (request and/or response) are validated.</summary>
    public ValidationDirection Validate { get; set; } = ValidationDirection.Both;
}
