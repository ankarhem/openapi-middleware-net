using System.Text.Json;

namespace OpenApiContractValidation.Models;

/// <summary>
/// The normalized representation of an outgoing HTTP response used for contract validation.
/// </summary>
public sealed record ParsedResponse
{
    /// <summary>The HTTP status code.</summary>
    public required int StatusCode { get; init; }

    /// <summary>The Content-Type header value, when present.</summary>
    public string? ContentType { get; init; }

    /// <summary>Response headers keyed by name, each with its list of values.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; init; }

    /// <summary>The parsed JSON response body, or <see langword="null"/> when no body was present.</summary>
    public JsonElement? Body { get; init; }

    /// <summary>The raw response body text, when available.</summary>
    public string? RawBody { get; init; }

    /// <summary>Whether the response carried a body.</summary>
    public required bool HasBody { get; init; }
}
