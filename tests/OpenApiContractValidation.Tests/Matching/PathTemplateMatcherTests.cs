using OpenApiContractValidation.Matching;
using Xunit;

namespace OpenApiContractValidation.Tests.Matching;

public class PathTemplateMatcherTests
{
    [Fact]
    public void LiteralBeatsParameter()
    {
        var matcher = new PathTemplateMatcher(new[] { "/users/{id}", "/users/me" });

        var matched = matcher.TryMatch("/users/me", out var template, out var captures);

        Assert.True(matched);
        Assert.NotNull(template);
        Assert.Equal("/users/me", template!.Template);
        Assert.Empty(captures);
    }

    [Fact]
    public void ParameterCaptured()
    {
        var matcher = new PathTemplateMatcher(new[] { "/users/{id}", "/users/me" });

        var matched = matcher.TryMatch("/users/42", out var template, out var captures);

        Assert.True(matched);
        Assert.NotNull(template);
        Assert.Equal("/users/{id}", template!.Template);
        Assert.Single(captures);
        Assert.Equal("42", captures["id"]);
    }

    [Fact]
    public void NoMatch_ReturnsFalse()
    {
        var matcher = new PathTemplateMatcher(new[] { "/users/{id}", "/users/me" });

        var matched = matcher.TryMatch("/nope", out var template, out var captures);

        Assert.False(matched);
        Assert.Null(template);
        Assert.Empty(captures);
    }

    [Fact]
    public void ParamDoesNotMatchAcrossSlash()
    {
        var matcher = new PathTemplateMatcher(new[] { "/files/{name}" });

        var matched = matcher.TryMatch("/files/a/b", out var template, out var captures);

        Assert.False(matched);
        Assert.Null(template);
        Assert.Empty(captures);
    }

    [Fact]
    public void MultiParam()
    {
        var matcher = new PathTemplateMatcher(new[] { "/orgs/{org}/repos/{repo}" });

        var matched = matcher.TryMatch(
            "/orgs/acme/repos/widgets",
            out var template,
            out var captures
        );

        Assert.True(matched);
        Assert.NotNull(template);
        Assert.Equal("/orgs/{org}/repos/{repo}", template!.Template);
        Assert.Equal("acme", captures["org"]);
        Assert.Equal("widgets", captures["repo"]);
    }

    [Fact]
    public void TrailingSlashStrict()
    {
        var matcher = new PathTemplateMatcher(new[] { "/users" });

        Assert.False(matcher.TryMatch("/users/", out _, out _));
        Assert.True(matcher.TryMatch("/users", out var template, out _));
        Assert.Equal("/users", template!.Template);
    }

    [Fact]
    public void CaseSensitive()
    {
        var matcher = new PathTemplateMatcher(new[] { "/Users" });

        var matched = matcher.TryMatch("/users", out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void PathTemplate_ParameterNames_ReturnsDeclaredParameters()
    {
        var template = new PathTemplate("/users/{id}/posts/{postId}");

        Assert.Equal(new[] { "id", "postId" }, template.ParameterNames);
    }

    [Fact]
    public void PathTemplate_ParameterNames_EmptyWhenNoParams()
    {
        var template = new PathTemplate("/health");

        Assert.Empty(template.ParameterNames);
    }

    [Fact]
    public void PathTemplate_SpecificityKey_ReflectsLiteralVsParam()
    {
        var template = new PathTemplate("/users/{id}");

        // ["", "users", "{id}"] → kinds [1, 1, 0] (literal, literal, parameter)
        Assert.Equal(new[] { 1, 1, 0 }, template.SpecificityKey);
    }

    [Fact]
    public void SpecificityComparer_SameReference_ReturnsZero()
    {
        var comparer = GetSpecificityComparer();
        var template = new PathTemplate("/a");

        Assert.Equal(0, comparer.Compare(template, template));
    }

    [Fact]
    public void SpecificityComparer_NullX_ReturnsNegative()
    {
        var comparer = GetSpecificityComparer();
        var template = new PathTemplate("/a");

        Assert.True(comparer.Compare(null!, template) < 0);
    }

    [Fact]
    public void SpecificityComparer_NullY_ReturnsPositive()
    {
        var comparer = GetSpecificityComparer();
        var template = new PathTemplate("/a");

        Assert.True(comparer.Compare(template, null!) > 0);
    }

    [Fact]
    public void SpecificityComparer_DifferentSegmentCounts_PrefersMoreSegments()
    {
        var comparer = GetSpecificityComparer();
        var longer = new PathTemplate("/a/b/c");
        var shorter = new PathTemplate("/a/b");

        Assert.True(comparer.Compare(longer, shorter) > 0);
        Assert.True(comparer.Compare(shorter, longer) < 0);
    }

    [Fact]
    public void SpecificityComparer_EqualTemplates_ReturnsZero()
    {
        var comparer = GetSpecificityComparer();
        var a = new PathTemplate("/users/{id}");
        var b = new PathTemplate("/users/{id}");

        Assert.Equal(0, comparer.Compare(a, b));
    }

    private static IComparer<PathTemplate> GetSpecificityComparer()
    {
        var assembly = typeof(PathTemplateMatcher).Assembly;
        var comparerType = assembly.GetType(
            "OpenApiContractValidation.Matching.PathTemplateSpecificityComparer"
        )!;
        var instanceField = comparerType.GetField("Instance")!;
        return (IComparer<PathTemplate>)instanceField.GetValue(null)!;
    }
}
