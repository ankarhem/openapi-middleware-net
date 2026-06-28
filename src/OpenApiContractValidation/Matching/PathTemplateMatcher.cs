using System.Collections.ObjectModel;

namespace OpenApiContractValidation.Matching;

/// <summary>
/// Resolves an incoming URL path to the single OpenAPI path template it matches, selecting the most
/// specific template when several could match.
/// </summary>
/// <remarks>
/// Templates are pre-sorted by specificity descending, so a literal segment always wins over a
/// parameter segment when both could match the same path (for example <c>/users/me</c> is preferred
/// over <c>/users/{id}</c>). The matcher is independent of ASP.NET Core routing: the OpenAPI
/// contract is the sole source of truth.
/// </remarks>
public sealed class PathTemplateMatcher
{
    /// <summary>A shared, empty captures dictionary returned when no template matches.</summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyCaptures =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(0, StringComparer.Ordinal)
        );

    private readonly List<PathTemplate> _orderedTemplates;

    /// <summary>
    /// Creates a matcher for the supplied OpenAPI path templates.
    /// </summary>
    /// <param name="templates">
    /// The OpenAPI Paths keys (for example <c>/users/{id}</c>). Order is irrelevant: templates are
    /// compiled and pre-sorted by specificity.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="templates"/> is <see langword="null"/>.</exception>
    public PathTemplateMatcher(IEnumerable<string> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);

        _orderedTemplates = templates
            .Select(t => new PathTemplate(t))
            .OrderByDescending(t => t, PathTemplateSpecificityComparer.Instance)
            .ToList();
    }

    /// <summary>
    /// Attempts to match <paramref name="urlDecodedPath"/> against the registered templates,
    /// returning the first (most specific) match.
    /// </summary>
    /// <param name="urlDecodedPath">
    /// The URL-decoded request path. The caller is responsible for decoding; this method does not
    /// decode again.
    /// </param>
    /// <param name="template">
    /// When this method returns <see langword="true"/>, the matched template; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <param name="captures">
    /// When this method returns <see langword="true"/>, the captured path parameters keyed by their
    /// OpenAPI name; otherwise an empty dictionary.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a template matches; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="urlDecodedPath"/> is <see langword="null"/>.</exception>
    public bool TryMatch(
        string urlDecodedPath,
        out PathTemplate? template,
        out IReadOnlyDictionary<string, string> captures
    )
    {
        ArgumentNullException.ThrowIfNull(urlDecodedPath);

        foreach (var candidate in _orderedTemplates)
        {
            if (candidate.TryMatch(urlDecodedPath, out var candidateCaptures))
            {
                template = candidate;
                captures = candidateCaptures;
                return true;
            }
        }

        template = null;
        captures = EmptyCaptures;
        return false;
    }
}

/// <summary>
/// Compares <see cref="PathTemplate"/> instances by specificity so that more specific templates sort
/// first: more segments rank above fewer, then literal segments (1) rank above parameter segments (0)
/// when compared left-to-right.
/// </summary>
internal sealed class PathTemplateSpecificityComparer : IComparer<PathTemplate>
{
    /// <summary>Singleton comparer instance.</summary>
    public static readonly PathTemplateSpecificityComparer Instance = new();

    private PathTemplateSpecificityComparer() { }

    /// <summary>
    /// Compares two templates and returns a value indicating which is more specific.
    /// </summary>
    /// <param name="x">The first template, or <see langword="null"/>.</param>
    /// <param name="y">The second template, or <see langword="null"/>.</param>
    /// <returns>
    /// A negative value when <paramref name="x"/> is more specific than <paramref name="y"/>; a
    /// positive value when <paramref name="y"/> is more specific; zero when equally specific.
    /// </returns>
    public int Compare(PathTemplate? x, PathTemplate? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var xKey = x.SpecificityKey;
        var yKey = y.SpecificityKey;

        var lengthComparison = xKey.Count.CompareTo(yKey.Count);
        if (lengthComparison != 0)
        {
            return lengthComparison;
        }

        var common = xKey.Count;
        for (var i = 0; i < common; i++)
        {
            var segmentComparison = xKey[i].CompareTo(yKey[i]);
            if (segmentComparison != 0)
            {
                return segmentComparison;
            }
        }

        return 0;
    }
}
