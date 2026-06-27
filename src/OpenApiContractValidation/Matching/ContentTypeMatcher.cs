using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace OpenApiContractValidation.Matching;

/// <summary>
/// Matches an actual HTTP <c>Content-Type</c> header against the media-type keys
/// declared in an OpenAPI operation's <c>content</c> map. Used for both request and
/// response body content-type validation.
/// </summary>
/// <remarks>
/// <para>
/// Comparison rules:
/// <list type="bullet">
/// <item>The type and subtype of both values are compared case-insensitively (ordinal-ignore-case).</item>
/// <item>Parameters (charset, q, boundary, version, ...) on the <c>actual</c> value are ignored
/// unless the spec key itself declares them, in which case they must match
/// (case-insensitive name, ordinal-ignore-case value).</item>
/// <item>Wildcards are supported <em>only</em> on the spec key:
/// <list type="bullet">
/// <item><c>*/*</c> matches any media type.</item>
/// <item><c>application/*</c> matches any subtype under <c>application</c>.</item>
/// <item><c>application/*+json</c> is a structured-suffix wildcard: it matches any subtype
/// whose structured suffix is <c>+json</c> (e.g. <c>application/vnd.api+json</c>).
/// A plain subtype such as <c>application/json</c> has no suffix and does <em>not</em> match.</item>
/// </list>
/// </item>
/// <item>When multiple spec keys match, the most specific one wins
/// (exact &gt; structured-suffix wildcard &gt; subtype wildcard &gt; <c>*/*</c>) and is returned in
/// <see cref="TryMatch(string?, System.Collections.Generic.IEnumerable{string}, out string?)"/>.</item>
/// </list>
/// </para>
/// </remarks>
public static class ContentTypeMatcher
{
    private const int SpecificityExact = 4;
    private const int SpecificitySuffixWildcard = 3;
    private const int SpecificitySubtypeWildcard = 2;
    private const int SpecificityAny = 1;

    /// <summary>
    /// Attempts to match <paramref name="actualContentType"/> against the most specific
    /// compatible key in <paramref name="specMediaTypeKeys"/>.
    /// </summary>
    /// <param name="actualContentType">
    /// The actual HTTP <c>Content-Type</c> header value. May be <see langword="null"/> or empty,
    /// in which case a match is only reported when one of the spec keys is <c>*/*</c>.
    /// </param>
    /// <param name="specMediaTypeKeys">
    /// The media-type keys declared by the OpenAPI contract (e.g. <c>application/json</c>,
    /// <c>application/*+json</c>, <c>*/*</c>).
    /// </param>
    /// <param name="matchedKey">
    /// When this method returns <see langword="true"/>, the most specific spec key that matched;
    /// otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if any spec key matched; otherwise <see langword="false"/>.
    /// </returns>
    public static bool TryMatch(
        string? actualContentType,
        IEnumerable<string> specMediaTypeKeys,
        out string? matchedKey
    )
    {
        ArgumentNullException.ThrowIfNull(specMediaTypeKeys);

        matchedKey = null;

        var actualIsEmpty = string.IsNullOrWhiteSpace(actualContentType);
        MediaTypeHeaderValue? actual = null;
        if (!actualIsEmpty && !MediaTypeHeaderValue.TryParse(actualContentType, out actual))
        {
            // The actual content-type could not be parsed; nothing can match it.
            return false;
        }

        var bestSpecificity = -1;
        string? bestKey = null;

        foreach (var rawKey in specMediaTypeKeys)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                continue;
            }

            if (!MediaTypeHeaderValue.TryParse(rawKey, out var spec))
            {
                continue;
            }

            int specificity;
            if (actualIsEmpty)
            {
                // Only the catch-all wildcard can match a missing content-type.
                specificity =
                    (spec.MatchesAllTypes && spec.MatchesAllSubTypes) ? SpecificityAny : 0;
            }
            else
            {
                (bool matches, specificity) = MatchAgainstSpec(actual!, spec);
                if (!matches)
                {
                    continue;
                }
            }

            if (specificity > bestSpecificity)
            {
                bestSpecificity = specificity;
                bestKey = rawKey;
            }
        }

        if (bestKey is null)
        {
            return false;
        }

        matchedKey = bestKey;
        return true;
    }

    /// <summary>
    /// Determines whether <paramref name="actual"/> matches the single spec key
    /// <paramref name="specKey"/>. Convenience overload for callers (and tests) that
    /// only need a boolean answer for one candidate.
    /// </summary>
    /// <param name="actual">The actual HTTP <c>Content-Type</c> header value.</param>
    /// <param name="specKey">The single OpenAPI media-type key to match against.</param>
    /// <returns><see langword="true"/> if <paramref name="specKey"/> matches; otherwise <see langword="false"/>.</returns>
    public static bool IsMatch(string? actual, string specKey)
    {
        return TryMatch(actual, new[] { specKey }, out _);
    }

    private static (bool Matches, int Specificity) MatchAgainstSpec(
        MediaTypeHeaderValue actual,
        MediaTypeHeaderValue spec
    )
    {
        int specificity;

        if (spec.MatchesAllTypes && spec.MatchesAllSubTypes)
        {
            specificity = SpecificityAny;
        }
        else
        {
            // The type component must match (case-insensitive). For */* this is skipped above.
            if (!actual.Type.Equals(spec.Type, StringComparison.OrdinalIgnoreCase))
            {
                return (false, 0);
            }

            if (spec.MatchesAllSubTypes)
            {
                // e.g. application/*  (strict: SubType is exactly "*")
                specificity = SpecificitySubtypeWildcard;
            }
            else if (spec.MatchesAllSubTypesWithoutSuffix && spec.Suffix.HasValue)
            {
                // Structured-suffix wildcard, e.g. application/*+json.
                // MatchesAllSubTypesWithoutSuffix is true (SubTypeWithoutSuffix is "*") while
                // MatchesAllSubTypes is false (SubType is "*+json"), so this branch only fires
                // for the suffix form. The actual value must carry the same structured suffix
                // (e.g. +json); a plain subtype like "json" has no suffix and must not match.
                if (
                    !actual.Suffix.HasValue
                    || !actual.Suffix.Value.Equals(
                        spec.Suffix.Value,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return (false, 0);
                }

                specificity = SpecificitySuffixWildcard;
            }
            else
            {
                // Exact subtype comparison (case-insensitive).
                if (!actual.SubType.Equals(spec.SubType, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, 0);
                }

                specificity = SpecificityExact;
            }
        }

        // Enforce only the parameters that the spec key explicitly declares.
        // Extra parameters on the actual value (e.g. charset) are ignored when the spec
        // does not name them.
        if (spec.Parameters is { Count: > 0 })
        {
            foreach (var specParam in spec.Parameters)
            {
                var paramName = specParam.Name;
                var found = false;
                StringSegment actualValue = default;

                if (actual.Parameters is { Count: > 0 })
                {
                    foreach (var actualParam in actual.Parameters)
                    {
                        if (actualParam.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            actualValue = actualParam.Value;
                            break;
                        }
                    }
                }

                if (
                    !found
                    || !actualValue.Equals(specParam.Value, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return (false, 0);
                }
            }
        }

        return (true, specificity);
    }
}
