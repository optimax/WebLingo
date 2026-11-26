using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
namespace WebLingo.Hosting.DotNet;

/// <summary>
/// The proxies a site trusts (specification, section 9, "M0.23 parts", part 4; the owner's answer, A1 (a) of "M0.23, Part 4:
/// Behind a Proxy"): <c>wasp.config</c>'s <c>"proxies"</c>, each an address, <c>127.0.0.1</c>, or a range, <c>10.0.0.0/8</c>,
/// none by default. A request whose connection comes from one of them takes its client and its scheme from its
/// <c>Forwarded</c> (RFC 7239), or, without one, from its <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c>: its client
/// the nearest address that is not a trusted proxy's, read from the last hop back, its scheme the one that hop's proxy
/// gives. Any other request's client is its connection's, its scheme its own. Its hosts, <c>wasp serve</c>'s, ASP.NET Core's
/// and WebLingo's own, read it; behind FastCGI, the web server gives the client.
/// </summary>
public sealed class WebLingoProxies
{
    private static readonly ConditionalWeakTable<WebLingoApplication, WebLingoProxies> Sites = new();

    private readonly List<(byte[] Network, int Bits)> _ranges;

    private WebLingoProxies(List<(byte[] Network, int Bits)> ranges) => _ranges = ranges;

    /// <summary>None trusted.</summary>
    public static WebLingoProxies None { get; } = new([]);

    /// <summary>An application's, its <see cref="WebLingoApplication.Proxies"/> read once, those it cannot read left out, as its load refused them.</summary>
    public static WebLingoProxies Of(WebLingoApplication application) =>
        Sites.GetValue(application, static site => TryParse(site.Proxies, out var proxies, out _) ? proxies : None);

    /// <summary>The proxies <paramref name="entries"/> name, each an address or a range; false, with why, for one that is neither.</summary>
    public static bool TryParse(IEnumerable<string> entries, out WebLingoProxies proxies, out string? error)
    {
        var ranges = new List<(byte[], int)>();
        proxies = None;
        foreach (var entry in entries)
        {
            var slash = entry.IndexOf('/');
            var text = slash < 0 ? entry : entry[..slash];
            if (!IPAddress.TryParse(text, out var address) || text.Contains('%'))
            {
                error = $"'{entry}' is not an address, 127.0.0.1 or ::1, nor a range, 10.0.0.0/8.";
                return false;
            }
            var bytes = Normal(address).GetAddressBytes();
            var bits = bytes.Length * 8;
            if (slash >= 0 && (!int.TryParse(entry[(slash + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                    out bits) || bits > bytes.Length * 8))
            {
                error = $"'{entry}' is not a range: its prefix is a number of bits from 0 to {bytes.Length * 8}.";
                return false;
            }
            ranges.Add((bytes, bits));
        }
        (proxies, error) = (new WebLingoProxies(ranges), null);
        return true;
    }

    /// <summary>Whether a connection from <paramref name="address"/> is a trusted proxy's.</summary>
    public bool Trusts(IPAddress address)
    {
        var bytes = Normal(address).GetAddressBytes();
        foreach (var (network, bits) in _ranges)
        {
            if (network.Length == bytes.Length && Within(bytes, network, bits)) return true;
        }
        return false;
    }

    /// <summary>
    /// A request's client and scheme: from its forwarding headers, each name's values as <paramref name="headers"/> gives
    /// them, where its connection comes from <paramref name="peer"/>, a trusted proxy; its peer's and
    /// <paramref name="scheme"/> otherwise. Its client as text, an IPv4 address mapped into IPv6 written as IPv4's; empty for
    /// no peer.
    /// </summary>
    public (string Client, string Scheme) Resolve(IPAddress? peer, string scheme, Func<string, IEnumerable<string>> headers)
    {
        if (peer is null) return ("", scheme);
        if (_ranges.Count == 0 || !Trusts(peer)) return (Text(peer), scheme);
        var hops = Hops(headers);
        if (hops.Count == 0) return (Text(peer), scheme);
        for (var i = hops.Count - 1; i >= 0; i--)
        {
            var (address, proto) = hops[i];
            var parsed = Address(address);
            // A hop that names no address, "unknown" or an obfuscated one, is where what can be known ends.
            if (parsed is null) return (Text(peer), ProtoOr(proto, scheme));
            if (i > 0 && Trusts(parsed)) continue;
            return (Text(parsed), ProtoOr(proto, scheme));
        }
        return (Text(peer), scheme);
    }

    /// <summary>The hops a request's forwarding headers name, the nearest last: <c>Forwarded</c>'s elements, else <c>X-Forwarded-For</c>'s addresses with <c>X-Forwarded-Proto</c>'s schemes, where they pair.</summary>
    private static List<(string Address, string? Proto)> Hops(Func<string, IEnumerable<string>> headers)
    {
        var hops = new List<(string, string?)>();
        var forwarded = headers("Forwarded").ToList();
        if (forwarded.Count > 0)
        {
            foreach (var element in SplitOutsideQuotes(string.Join(",", forwarded), ','))
            {
                string? @for = null, proto = null;
                foreach (var pair in SplitOutsideQuotes(element, ';'))
                {
                    var equals = pair.IndexOf('=');
                    if (equals <= 0) continue;
                    var name = pair[..equals].Trim();
                    var value = Unquote(pair[(equals + 1)..].Trim());
                    if (name.Equals("for", StringComparison.OrdinalIgnoreCase)) @for = value;
                    else if (name.Equals("proto", StringComparison.OrdinalIgnoreCase)) proto = value;
                }
                if (@for is not null) hops.Add((@for, proto));
            }
            return hops;
        }
        var addresses = headers("X-Forwarded-For").SelectMany(value => value.Split(',')).Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
        var protos = headers("X-Forwarded-Proto").SelectMany(value => value.Split(',')).Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
        for (var i = 0; i < addresses.Count; i++)
        {
            var proto = protos.Count == addresses.Count ? protos[i] : protos.Count > 0 ? protos[^1] : null;
            hops.Add((addresses[i], proto));
        }
        return hops;
    }

    private static string ProtoOr(string? proto, string scheme) =>
        proto?.ToLowerInvariant() is "http" or "https" ? proto.ToLowerInvariant() : scheme;

    /// <summary>A hop's address, its brackets and port left out: <c>192.0.2.43:4711</c>, <c>[2001:db8::17]:4711</c>; null where it names none.</summary>
    private static IPAddress? Address(string text)
    {
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            return close > 1 && IPAddress.TryParse(text[1..close], out var six) ? six : null;
        }
        if (IPAddress.TryParse(text, out var address)) return address;
        var colon = text.LastIndexOf(':');
        return colon > 0 && text.IndexOf(':') == colon && IPAddress.TryParse(text[..colon], out var four) ? four : null;
    }

    private static IPAddress Normal(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static string Text(IPAddress address)
    {
        var normal = Normal(address);
        if (normal.AddressFamily == AddressFamily.InterNetworkV6 && normal.ScopeId != 0) normal = new IPAddress(normal.GetAddressBytes());
        return normal.ToString();
    }

    private static bool Within(byte[] address, byte[] network, int bits)
    {
        for (var i = 0; i < address.Length && bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (byte)(0xFF << (8 - bits));
            if ((address[i] & mask) != (network[i] & mask)) return false;
        }
        return true;
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"') return value;
        return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static List<string> SplitOutsideQuotes(string text, char separator)
    {
        var parts = new List<string>();
        var (start, quoted) = (0, false);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && quoted)
            {
                i++;
                continue;
            }
            if (text[i] == '"') quoted = !quoted;
            else if (text[i] == separator && !quoted)
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(text[start..].Trim());
        return parts.Where(part => part.Length > 0).ToList();
    }
}
