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
}
