using System.Net;
using System.Net.Sockets;

namespace NetIndex.Providers.TextEmbeddingsInference;

/// <summary>Thrown inside the connect callback when a host resolves to a non-private address.</summary>
internal sealed class TeiNonPrivateAddressException : Exception
{
    internal TeiNonPrivateAddressException()
        : base("The reranker host resolved to a non-private address; the connection was refused.")
    {
    }
}

/// <summary>
/// The opt-in connect-time guard: resolve the host and connect only when every address is loopback, RFC 1918,
/// IPv6 unique-local (fc00::/7) or link-local.
/// </summary>
internal static class TeiPrivateNetworkGuard
{
    internal static bool IsPrivateAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        return (bytes[0] & 0xFE) == 0xFC || address.IsIPv6LinkLocal;
    }

    internal static Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);

    /// <summary>Resolves <paramref name="endPoint"/> and connects; refuses before any connection if one address is not private.</summary>
    internal static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endPoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        CancellationToken cancellationToken)
    {
        var addresses = await resolve(endPoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(a => !IsPrivateAddress(a)))
        {
            throw new TeiNonPrivateAddressException();
        }

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("Unable to connect to the reranker.", last);
    }
}
