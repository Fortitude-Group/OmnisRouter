using System.Net;
using System.Net.Sockets;

namespace OmnisRouter.Api.Routing;

/// <summary>
/// SSRF guard for the image materializer (security-review F1). Rejects requests that resolve to
/// non-public IP space so a client-supplied image URL cannot probe the operator's internal network.
/// </summary>
public static class SafeImageFetch
{
    public const int MaxBytes = 10 * 1024 * 1024; // 10 MB cap
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A connect callback that resolves the target host and connects only to a PUBLIC IP — validated
    /// at connect time, which also defeats DNS-rebinding (the IP we connect to is the IP we checked).
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        var target = Array.Find(addresses, IsPublic)
            ?? throw new HttpRequestException($"Refusing to fetch image from '{host}': resolves only to non-public address space.");

        var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, port), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>True only for globally-routable public unicast addresses.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            // 0.0.0.0/8, 10/8, 100.64/10 (CGNAT), 127/8, 169.254/16 (link-local),
            // 172.16/12, 192.0.0/24, 192.168/16, 224/4 (multicast), 240/4 (reserved).
            return b[0] switch
            {
                0 or 10 or 127 => false,
                100 when b[1] >= 64 && b[1] <= 127 => false,
                169 when b[1] == 254 => false,
                172 when b[1] >= 16 && b[1] <= 31 => false,
                192 when b[1] == 168 => false,
                192 when b[1] == 0 && b[2] == 0 => false,
                >= 224 => false, // multicast + reserved
                _ => true,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
            {
                return false;
            }

            var b = address.GetAddressBytes();
            return b[0] != 0x00; // exclude ::, ::1 handled by loopback, and other low reserved blocks
        }

        return false;
    }
}
