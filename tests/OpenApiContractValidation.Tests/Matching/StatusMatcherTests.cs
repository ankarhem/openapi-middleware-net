using OpenApiContractValidation.Matching;
using Xunit;

namespace OpenApiContractValidation.Tests.Matching;

public class StatusMatcherTests
{
    [Fact]
    public void ExactWins()
    {
        var keys = new[] { "200", "2XX", "default" };

        var matched = StatusMatcher.TryMatch(200, keys, out var matchedKey);

        Assert.True(matched);
        Assert.Equal("200", matchedKey);
    }

    [Fact]
    public void RangeWhenNoExact()
    {
        var keys = new[] { "2XX", "default" };

        Assert.True(StatusMatcher.TryMatch(200, keys, out var matchedKey200));
        Assert.Equal("2XX", matchedKey200);

        Assert.True(StatusMatcher.TryMatch(250, keys, out var matchedKey250));
        Assert.Equal("2XX", matchedKey250);
    }

    [Fact]
    public void DefaultWhenNoOther()
    {
        var keys = new[] { "2XX", "default" };

        var matched = StatusMatcher.TryMatch(404, keys, out var matchedKey);

        Assert.True(matched);
        Assert.Equal("default", matchedKey);
    }

    [Fact]
    public void NoMatch()
    {
        var keys = new[] { "200" };

        var matched = StatusMatcher.TryMatch(500, keys, out var matchedKey);

        Assert.False(matched);
        Assert.Null(matchedKey);
    }

    [Fact]
    public void RangeCaseInsensitive()
    {
        var keys = new[] { "2xx" };

        var matched = StatusMatcher.TryMatch(201, keys, out var matchedKey);

        Assert.True(matched);
        Assert.Equal("2xx", matchedKey);
    }

    [Fact]
    public void ExactOverRange_201()
    {
        var keys = new[] { "201", "2XX" };

        Assert.True(StatusMatcher.TryMatch(201, keys, out var matchedKey201));
        Assert.Equal("201", matchedKey201);

        Assert.True(StatusMatcher.TryMatch(202, keys, out var matchedKey202));
        Assert.Equal("2XX", matchedKey202);
    }

    [Fact]
    public void FiveXX()
    {
        var keys = new[] { "5XX", "default" };

        var matched = StatusMatcher.TryMatch(503, keys, out var matchedKey);

        Assert.True(matched);
        Assert.Equal("5XX", matchedKey);
    }

    [Fact]
    public void NullKey_IsIgnored()
    {
        var keys = new List<string> { null!, "200" };

        var matched = StatusMatcher.TryMatch(200, keys, out var matchedKey);

        Assert.True(matched);
        Assert.Equal("200", matchedKey);
    }

    [Fact]
    public void UnrecognizedThreeCharKey_IsIgnored()
    {
        // "ABC" has length 3 but is neither a valid range ("NXX") nor an exact code.
        var keys = new[] { "ABC", "200" };

        var matched = StatusMatcher.TryMatch(200, keys, out var matchedKey);

        Assert.True(matched);
        Assert.Equal("200", matchedKey);
    }

    [Fact]
    public void NonThreeCharKey_IsIgnored()
    {
        // "OK" has length != 3 and is not "default", so it is unrecognized.
        var keys = new[] { "OK", "200" };

        var matched = StatusMatcher.TryMatch(200, keys, out var matchedKey);

        Assert.True(matched);
        Assert.Equal("200", matchedKey);
    }
}
