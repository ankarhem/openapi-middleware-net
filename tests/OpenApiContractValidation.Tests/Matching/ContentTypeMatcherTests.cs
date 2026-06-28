using OpenApiContractValidation.Matching;
using Xunit;

namespace OpenApiContractValidation.Tests.Matching;

public class ContentTypeMatcherTests
{
    [Fact]
    public void ExactMatch()
    {
        Assert.True(ContentTypeMatcher.IsMatch("application/json", "application/json"));
    }

    [Fact]
    public void CharsetIgnoredOnActual()
    {
        // The spec key declares no parameters, so charset on the actual value is ignored.
        Assert.True(
            ContentTypeMatcher.IsMatch("application/json; charset=utf-8", "application/json")
        );
    }

    [Fact]
    public void SuffixWildcard()
    {
        Assert.True(ContentTypeMatcher.IsMatch("application/vnd.api+json", "application/*+json"));
        Assert.True(
            ContentTypeMatcher.IsMatch(
                "application/vnd.api+json; charset=utf-8",
                "application/*+json"
            )
        );
    }

    [Fact]
    public void SuffixWildcard_PlainJsonDoesNotMatchSuffix()
    {
        // application/json has no structured suffix and therefore must NOT match application/*+json.
        Assert.False(ContentTypeMatcher.IsMatch("application/json", "application/*+json"));
    }

    [Fact]
    public void SubtypeWildcard()
    {
        Assert.True(ContentTypeMatcher.IsMatch("application/json", "application/*"));
    }

    [Fact]
    public void AnyWildcard()
    {
        Assert.True(ContentTypeMatcher.IsMatch("text/plain", "*/*"));
    }

    [Fact]
    public void Mismatch()
    {
        Assert.False(ContentTypeMatcher.IsMatch("text/plain", "application/json"));
    }

    [Fact]
    public void TryMatch_PrefersMostSpecific()
    {
        var keys = new[] { "*/*", "application/json", "application/*" };

        var matched = ContentTypeMatcher.TryMatch("application/json", keys, out var matchedKey);

        Assert.True(matched);
        Assert.Equal("application/json", matchedKey);
    }

    [Fact]
    public void SpecParameterEnforced()
    {
        // The spec demands charset=utf-8; an actual value without it must not match.
        Assert.False(
            ContentTypeMatcher.IsMatch("application/json", "application/json; charset=utf-8")
        );
        // When the actual value carries the demanded parameter, it matches.
        Assert.True(
            ContentTypeMatcher.IsMatch(
                "application/json; charset=utf-8",
                "application/json; charset=utf-8"
            )
        );
    }

    [Fact]
    public void UnparseableActual_ReturnsFalse()
    {
        // ";" is not a valid media type (no type/subtype), so TryParse fails.
        // Using "*/*" as the spec key means a match would occur IF the actual were parseable.
        // The false return therefore proves the unparseable-actual early-exit path.
        Assert.False(ContentTypeMatcher.IsMatch(";", "*/*"));
    }

    [Fact]
    public void TryMatch_SkipsBlankSpecKeys()
    {
        // Null/empty/whitespace spec keys are silently skipped.
        var matched = ContentTypeMatcher.TryMatch(
            "application/json",
            new[] { "", "   ", null!, "application/json" },
            out var matchedKey
        );

        Assert.True(matched);
        Assert.Equal("application/json", matchedKey);
    }

    [Fact]
    public void TryMatch_SkipsUnparseableSpecKey()
    {
        // ";" is not a valid media type, so the spec key is skipped.
        // The valid key "application/json" still matches.
        var matched = ContentTypeMatcher.TryMatch(
            "application/json",
            new[] { ";", "application/json" },
            out var matchedKey
        );

        Assert.True(matched);
        Assert.Equal("application/json", matchedKey);
    }

    [Fact]
    public void NullActual_MatchesOnlyCatchAll()
    {
        // A null actual content-type matches only the "*/*" wildcard.
        var matched = ContentTypeMatcher.TryMatch(null, new[] { "*/*" }, out var matchedKey);

        Assert.True(matched);
        Assert.Equal("*/*", matchedKey);
    }

    [Fact]
    public void EmptyActual_NonWildcardGetsZeroSpecificity_CatchAllWins()
    {
        // Empty actual assigns specificity 0 to non-wildcard keys and SpecificityAny
        // (1) to "*/*". When both are present, "*/*" wins the specificity tie-break.
        var matched = ContentTypeMatcher.TryMatch(
            null,
            new[] { "application/json", "*/*" },
            out var matchedKey
        );

        Assert.True(matched);
        Assert.Equal("*/*", matchedKey);
    }

    [Fact]
    public void SpecParameter_ActualHasDifferentParam_NoMatch()
    {
        // Spec demands charset=utf-8, but the actual only carries a "boundary" parameter.
        // The inner foreach over actual.Parameters completes without finding a match.
        Assert.False(
            ContentTypeMatcher.IsMatch(
                "application/json; boundary=xyz",
                "application/json; charset=utf-8"
            )
        );
    }
}
