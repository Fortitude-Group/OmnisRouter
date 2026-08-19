using System.Net;
using OmnisRouter.Api.Routing;

namespace OmnisRouter.Api.Tests;

public class SsrfGuardTests
{
    [Theory]
    // Non-public → must be rejected (SSRF surface).
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)] // cloud metadata endpoint
    [InlineData("100.64.0.1", false)]       // CGNAT
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]        // multicast
    [InlineData("::1", false)]              // IPv6 loopback
    [InlineData("fe80::1", false)]          // IPv6 link-local
    [InlineData("fc00::1", false)]          // IPv6 unique-local
    [InlineData("::ffff:10.0.0.1", false)]  // IPv4-mapped private
    // Public → allowed.
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("172.15.0.1", true)]        // just below the 172.16/12 private block
    [InlineData("172.32.0.1", true)]        // just above it
    [InlineData("93.184.216.34", true)]     // example.com
    [InlineData("2606:4700:4700::1111", true)]
    public void IsPublic_classifies_addresses(string ip, bool expected)
    {
        Assert.Equal(expected, SafeImageFetch.IsPublic(IPAddress.Parse(ip)));
    }
}
