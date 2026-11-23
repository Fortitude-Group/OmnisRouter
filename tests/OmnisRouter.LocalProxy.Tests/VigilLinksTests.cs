using OmnisRouter.LocalProxy;

namespace OmnisRouter.LocalProxy.Tests;

public class VigilLinksTests
{
    [Theory]
    [InlineData(VigilLinks.Tray)]
    [InlineData(VigilLinks.Receipt)]
    [InlineData(VigilLinks.Setup)]
    public void Signup_is_the_hosted_signup_tagged_with_its_place(string medium)
    {
        Assert.Equal(
            $"https://app.omnisvigil.com/signup?utm_source=omnisrouter&utm_medium={medium}",
            VigilLinks.Signup(medium));
    }

    [Fact]
    public void The_three_places_have_distinct_tags()
    {
        Assert.Equal(3, new[] { VigilLinks.Tray, VigilLinks.Receipt, VigilLinks.Setup }.Distinct().Count());
    }

    [Theory]
    [InlineData("https://app.omnisvigil.com")]
    [InlineData("https://app.omnisvigil.com/")]
    [InlineData("  https://APP.omnisvigil.com/dashboard  ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void GetKey_sends_the_hosted_dashboard_through_tagged_signup(string? endpoint)
    {
        Assert.Equal(VigilLinks.Signup(VigilLinks.Setup), VigilLinks.GetKey(endpoint, VigilLinks.Setup));
    }

    [Theory]
    [InlineData("https://vigil.example.com", "https://vigil.example.com")]
    [InlineData(" http://localhost:5080/ ", "http://localhost:5080/")]
    [InlineData("https://app.omnisvigil.com.evil.example", "https://app.omnisvigil.com.evil.example")]
    public void GetKey_leaves_a_self_hosted_dashboard_untagged(string endpoint, string expected)
    {
        Assert.Equal(expected, VigilLinks.GetKey(endpoint, VigilLinks.Setup));
    }
}
