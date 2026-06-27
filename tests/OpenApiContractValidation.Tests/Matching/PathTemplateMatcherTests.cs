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
}
