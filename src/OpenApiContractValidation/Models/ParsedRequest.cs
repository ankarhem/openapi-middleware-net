using System.Text.Json;

namespace OpenApiContractValidation.Models;

/// <summary>
/// The normalized representation of an incoming HTTP request used for contract validation.
/// </summary>
public sealed record ParsedRequest
{
    /// <summary>The HTTP method (e.g. "GET", "POST").</summary>
    public required string Method { get; init; }

    /// <summary>The URL-decoded request path.</summary>
    public required string Path { get; init; }

    /// <summary>The Content-Type header value, when present.</summary>
    public string? ContentType { get; init; }

    /// <summary>Query parameters keyed by name, each with its list of values.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> QueryValues { get; init; }

    /// <summary>Request headers keyed by name, each with its list of values.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; init; }

    /// <summary>Cookies keyed by name.</summary>
    public required IReadOnlyDictionary<string, string> Cookies { get; init; }

    /// <summary>The parsed JSON request body, or <see langword="null"/> when no body was supplied.</summary>
    public JsonElement? Body { get; init; }

    /// <summary>The raw request body text, when available.</summary>
    public string? RawBody { get; init; }
}
