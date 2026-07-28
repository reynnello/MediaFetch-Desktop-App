using System.Net;
using System.Net.Sockets;

namespace MediaFetch.Api.Services;

public sealed class MediaUrlValidator : IMediaUrlValidator
{
    public async Task<UrlValidationResult> ValidateAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return UrlValidationResult.Invalid(
                "A public absolute HTTP or HTTPS URL without embedded credentials is required.");
        }

        if (uri.IsLoopback || uri.HostNameType is UriHostNameType.Unknown)
        {
            return UrlValidationResult.Invalid("Local and invalid hosts are not allowed.");
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
            if (addresses.Length == 0 || addresses.Any(IsNonPublicAddress))
            {
                return UrlValidationResult.Invalid(
                    "The URL must resolve only to public network addresses.");
            }
        }
        catch (Exception exception) when (
            exception is SocketException or ArgumentException)
        {
            return UrlValidationResult.Invalid("The URL host could not be resolved.");
        }

        return UrlValidationResult.Valid(uri);
    }

    internal static bool IsNonPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        if (address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6
            && address.IsIPv4MappedToIPv6)
        {
            return IsNonPublicAddress(address.MapToIPv4());
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();

        return bytes[0] switch
        {
            0 or 10 or 127 => true,
            100 when bytes[1] is >= 64 and <= 127 => true,
            169 when bytes[1] == 254 => true,
            172 when bytes[1] is >= 16 and <= 31 => true,
            192 when bytes[1] == 0 => true,
            192 when bytes[1] == 168 => true,
            198 when bytes[1] is 18 or 19 or 51 => true,
            >= 224 => true,
            _ => false
        };
    }
}
