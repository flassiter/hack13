using System.Net;
using System.Net.Sockets;

namespace Hack13.Contracts.Utilities;

public static class HttpEndpointGuard
{
    public static async Task<(bool IsAllowed, string? Error)> ValidateOutboundHttpEndpointAsync(
        string endpoint,
        bool allowPrivateNetwork,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return (false, $"Invalid absolute URL: '{endpoint}'.");

        if (uri.Scheme is not ("http" or "https"))
            return (false, $"Only http/https URLs are allowed. Received '{uri.Scheme}'.");

        if (string.IsNullOrWhiteSpace(uri.Host))
            return (false, "URL host is required.");

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            if (!allowPrivateNetwork)
                return (false, "Outbound requests to localhost are blocked. Set allow_private_network=true to enable.");
            return (true, null);
        }

        if (IPAddress.TryParse(uri.Host, out var ipLiteral))
        {
            if (!allowPrivateNetwork && IsPrivateOrLocalAddress(ipLiteral))
                return (false, $"Outbound requests to private/local address '{ipLiteral}' are blocked.");

            return (true, null);
        }

        IPAddress[] resolvedAddresses;
        try
        {
            resolvedAddresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
        }
        catch (Exception ex)
        {
            return (false, $"Failed to resolve host '{uri.Host}': {ex.Message}");
        }

        if (resolvedAddresses.Length == 0)
            return (false, $"Host '{uri.Host}' resolved to no addresses.");

        if (!allowPrivateNetwork && resolvedAddresses.Any(IsPrivateOrLocalAddress))
        {
            return (
                false,
                $"Outbound requests to private/local host '{uri.Host}' are blocked. Set allow_private_network=true to enable.");
        }

        return (true, null);
    }

    private static bool IsPrivateOrLocalAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal || ip.Equals(IPAddress.IPv6None))
                return true;

            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC) // fc00::/7
                return true;

            return false;
        }

        var octets = ip.GetAddressBytes();
        if (octets.Length != 4)
            return false;

        return octets[0] switch
        {
            0 => true,
            10 => true,
            127 => true,
            169 when octets[1] == 254 => true, // link-local
            172 when octets[1] >= 16 && octets[1] <= 31 => true,
            192 when octets[1] == 168 => true,
            _ => false
        };
    }
}
