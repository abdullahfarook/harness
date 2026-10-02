using System.Net;
using System.Net.Sockets;

namespace Harness.Browser;

public sealed class UrlPolicy(Uri origin, Func<string,Task<IPAddress[]>>? resolver = null)
{
    public static void ValidateMethod(string method)
    { if (method is not ("GET" or "HEAD")) { throw new InvalidOperationException("Read-only browsing blocks mutating HTTP requests."); } }
    private readonly Func<string,Task<IPAddress[]>> resolve = resolver ?? Dns.GetHostAddressesAsync;
    public async Task<Uri> ValidateAsync(string value, bool navigation)
    {
        if (!Uri.TryCreate(value,UriKind.Absolute,out Uri? uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) { throw new InvalidOperationException("Only public HTTP(S) URLs without credentials are permitted."); }
        if (navigation && (uri.Scheme != origin.Scheme || uri.Host != origin.Host || uri.Port != origin.Port)) { throw new InvalidOperationException($"Navigation must stay on the target origin: {uri.AbsoluteUri}"); }
        IPAddress[] addresses = await resolve(uri.DnsSafeHost);
        if (addresses.Length == 0 || addresses.Any(ip => !IsPublic(ip))) { throw new InvalidOperationException($"Private or unresolved destination blocked: {uri.Host}"); }
        return uri;
    }
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) { address=address.MapToIPv4(); }
        if (IPAddress.IsLoopback(address)) { return false; }
        byte[] b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6) { return !address.Equals(IPAddress.IPv6Any) && !address.IsIPv6LinkLocal && !address.IsIPv6Multicast && !address.IsIPv6SiteLocal && (b[0]&0xfe) != 0xfc && !(b[0]==0x20 && b[1]==0x01 && b[2]==0x0d && b[3]==0xb8); }
        return b[0] is not (0 or 10 or 127) && b[0] < 224 && !(b[0]==169 && b[1]==254) && !(b[0]==172 && b[1]>=16 && b[1]<=31) && !(b[0]==192 && (b[1]==168 || b[1]==0 || (b[1]==2))) && !(b[0]==100 && b[1]>=64 && b[1]<=127) && !(b[0]==198 && b[1] is 18 or 19 or 51) && !(b[0]==203 && b[1]==0 && b[2]==113);
    }
}
