using Heracles.Model;

namespace Heracles.Tests;

public sealed class NavigationPolicyTests
{
    [Fact]
    public void WebLinksOpenInTheBrowser()
    {
        Assert.Equal(NavigationDecision.OpenExternally, NavigationPolicy.Decide("https://example.com/a"));
        Assert.Equal(NavigationDecision.OpenExternally, NavigationPolicy.Decide("http://example.com"));
        Assert.Equal(NavigationDecision.OpenExternally, NavigationPolicy.Decide("mailto:someone@example.com"));
    }

    [Theory]
    [InlineData("file:///tmp/evil.html")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("heracles-image://local/abc")] // an image is not a place to go
    [InlineData("heracles://elsewhere/abc")] // not a link to a note
    [InlineData("about:blank")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void EverythingElseIsRefused(string? text)
    {
        Assert.Equal(NavigationDecision.Deny, NavigationPolicy.Decide(text));
        Assert.Equal(NavigationDecision.Deny, NavigationPolicy.Decide((Uri?)null));
    }

    [Fact]
    public void AppLinksAreHandedToTheApp()
    {
        Assert.Equal(NavigationDecision.OpenInApp, NavigationPolicy.Decide("heracles://note/abc"));
    }
}

public sealed class AppLinkTests
{
    [Fact]
    public void ANoteLinkNamesItsNote()
    {
        Assert.Equal(new AppLink("abc-123", null), AppLink.Parse(new Uri("heracles://note/abc-123")));
    }

    [Fact]
    public void ABlockLinkNamesItsBlockToo()
    {
        Assert.Equal(new AppLink("abc-123", "b-9"), AppLink.Parse("heracles://note/abc-123#block=b-9"));
    }

    [Theory]
    [InlineData("https://note/abc")]
    [InlineData("heracles://note/")]
    [InlineData("heracles://other/abc")]
    [InlineData("heracles://note/a/b")]
    public void OtherURLsAreNotAppLinks(string text)
    {
        Assert.Null(AppLink.Parse(text));
    }

    [Fact]
    public void ALinkWritesBackToTheSameURL()
    {
        Assert.Equal("heracles://note/abc", new AppLink("abc", null).Url);
        Assert.Equal("heracles://note/abc#block=b1", new AppLink("abc", "b1").Url);
    }

    [Fact]
    public void AnEmptyBlockIsNoBlock()
    {
        Assert.Equal(new AppLink("abc", null), AppLink.Parse("heracles://note/abc#block="));
    }
}
