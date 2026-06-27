namespace OpenApiContractValidation.Matching;

/// <summary>
/// Matches an actual HTTP status code against the response keys declared in an
/// OpenAPI operation's <c>responses</c> map.
/// </summary>
/// <remarks>
/// OpenAPI response keys may be an exact three-digit status code (e.g. "200"),
/// a status-code range class ("1XX" through "5XX", case-insensitive), or the
/// literal "default". This matcher selects the single most specific matching key
/// according to OpenAPI precedence so that callers can decide whether an
/// observed status code is documented by the contract.
/// </remarks>
public static class StatusMatcher
{
    /// <summary>
    /// Attempts to match <paramref name="statusCode"/> against the supplied
    /// <paramref name="responseKeys"/> using OpenAPI response-key semantics.
    /// </summary>
    /// <param name="statusCode">The actual HTTP status code (e.g. 200, 404).</param>
    /// <param name="responseKeys">
    /// The response keys declared on an OpenAPI operation. Each key may be an
    /// exact three-digit code ("200"), a range class ("1XX"–"5XX",
    /// case-insensitive), or the literal "default".
    /// </param>
    /// <param name="matchedKey">
    /// When this method returns <see langword="true"/>, the best (most specific)
    /// matching key exactly as it appeared in <paramref name="responseKeys"/>;
    /// when this method returns <see langword="false"/>, <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if any key matched <paramref name="statusCode"/>;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Precedence is most-specific-wins, in the following order:
    /// <list type="number">
    ///   <item><description>Exact code match (e.g. status 200 against key "200").</description></item>
    ///   <item><description>Range class match (e.g. 200–299 against key "2XX").</description></item>
    ///   <item><description>The literal "default" key, which matches any status code.</description></item>
    /// </list>
    /// When no key matches at any tier, the method returns <see langword="false"/>
    /// and <paramref name="matchedKey"/> is set to <see langword="null"/>, which
    /// lets callers raise an "undocumented status" violation.
    /// </remarks>
    public static bool TryMatch(
        int statusCode,
        IEnumerable<string> responseKeys,
        out string? matchedKey
    )
    {
        ArgumentNullException.ThrowIfNull(responseKeys);

        string? rangeKey = null;
        string? defaultKey = null;

        foreach (var key in responseKeys)
        {
            if (key is null)
            {
                continue;
            }

            var (kind, value) = ParseKey(key);

            // An exact code match is the most specific tier: short-circuit immediately.
            if (kind == ResponseKeyKind.Exact && value == statusCode)
            {
                matchedKey = key;
                return true;
            }

            if (kind == ResponseKeyKind.Range && MatchesRange(statusCode, value))
            {
                rangeKey ??= key;
            }
            else if (kind == ResponseKeyKind.Default)
            {
                defaultKey ??= key;
            }
        }

        // Range class beats "default".
        matchedKey = rangeKey ?? defaultKey;
        return matchedKey is not null;
    }

    /// <summary>
    /// Classifies an OpenAPI response key and extracts its numeric value.
    /// </summary>
    /// <param name="key">The raw response key (assumed non-null).</param>
    /// <returns>
    /// A tuple of the key kind and its value: for an exact code the integer
    /// status (e.g. 200), for a range class the hundreds digit (e.g. 2 for
    /// "2XX"), and zero for "default" or unrecognized keys.
    /// </returns>
    private static (ResponseKeyKind Kind, int Value) ParseKey(string key)
    {
        if (key.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return (ResponseKeyKind.Default, 0);
        }

        if (key.Length == 3)
        {
            // Range class: a single digit followed by "XX" (case-insensitive), e.g. "2XX".
            if (IsDigit(key[0]) && IsX(key[1]) && IsX(key[2]))
            {
                return (ResponseKeyKind.Range, key[0] - '0');
            }

            // Exact three-digit code, e.g. "200".
            if (IsDigit(key[0]) && IsDigit(key[1]) && IsDigit(key[2]))
            {
                return (
                    ResponseKeyKind.Exact,
                    (key[0] - '0') * 100 + (key[1] - '0') * 10 + (key[2] - '0')
                );
            }
        }

        return (ResponseKeyKind.None, 0);
    }

    /// <summary>
    /// Determines whether <paramref name="statusCode"/> falls within the range
    /// class described by <paramref name="rangeClass"/> (e.g. 2 → 200–299).
    /// </summary>
    private static bool MatchesRange(int statusCode, int rangeClass) =>
        statusCode / 100 == rangeClass;

    private static bool IsDigit(char c) => (uint)(c - '0') < 10u;

    private static bool IsX(char c) => c is 'X' or 'x';

    private enum ResponseKeyKind
    {
        None,
        Exact,
        Range,
        Default,
    }
}
