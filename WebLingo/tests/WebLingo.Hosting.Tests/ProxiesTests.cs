using System.Net;
using WebLingo.Hosting.DotNet;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// The proxies a site trusts (specification, section 9, "M0.23 parts", part 4; the owner's answer, A1 (a) of "M0.23, Part 4:
/// Behind a Proxy"): a request from one of them takes its client and its scheme from its <c>Forwarded</c>, else from its
/// <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c>, its client the nearest address that is not a trusted proxy's; any
/// other request's forwarding headers are not believed.
/// </summary>
public sealed class ProxiesTests
{
    private static WebLingoProxies Proxies(params string[] entries)
    {
        Assert.True(WebLingoProxies.TryParse(entries, out var proxies, out var error), error);
        return proxies;
    }

    private static (string, string) Resolve(WebLingoProxies proxies, string peer, params (string Name, string Value)[] headers) =>
        proxies.Resolve(IPAddress.Parse(peer), "http", name => headers.Where(header => header.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(header => header.Value));

    [Theory]
    [InlineData("tea", "'tea' is not an address, 127.0.0.1 or ::1, nor a range, 10.0.0.0/8.")]
    [InlineData("10.0.0.0/33", "'10.0.0.0/33' is not a range: its prefix is a number of bits from 0 to 32.")]
    [InlineData("10.0.0.0/x", "'10.0.0.0/x' is not a range")]
    [InlineData("fe80::1%3", "'fe80::1%3' is not an address")]
    public void AnEntryThatIsNeitherAnAddressNorARangeIsRefused(string entry, string error)
    {
        Assert.False(WebLingoProxies.TryParse([entry], out _, out var why));
        Assert.StartsWith(error, why, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUntrustedPeersForwardingHeadersAreNotBelieved()
    {
        Assert.Equal(("192.0.2.9", "http"), Resolve(WebLingoProxies.None, "192.0.2.9", ("X-Forwarded-For", "203.0.113.7"), ("X-Forwarded-Proto", "https")));
        Assert.Equal(("192.0.2.9", "http"), Resolve(Proxies("10.0.0.0/8"), "192.0.2.9", ("Forwarded", "for=203.0.113.7;proto=https")));
        Assert.Equal(("", "http"), WebLingoProxies.None.Resolve(null, "http", _ => []));
    }

    [Fact]
    public void ATrustedProxysClientIsTheNearestAddressNotATrustedProxys()
    {
        var proxies = Proxies("127.0.0.1", "10.0.0.0/8", "fd00::/8");
        Assert.Equal(("203.0.113.7", "https"), Resolve(proxies, "127.0.0.1", ("X-Forwarded-For", "203.0.113.7"), ("X-Forwarded-Proto", "https")));
        // Through two proxies, the client's address the one before them; one it names before that not believed.
        Assert.Equal(("198.51.100.1", "https"), Resolve(proxies, "10.1.2.3", ("X-Forwarded-For", "6.6.6.6, 198.51.100.1, 10.0.0.5"),
            ("X-Forwarded-Proto", "http, https, http")));
        Assert.Equal(("198.51.100.1", "https"), Resolve(proxies, "10.1.2.3", ("X-Forwarded-For", "198.51.100.1"), ("X-Forwarded-For", "10.0.0.5"),
            ("X-Forwarded-Proto", "https")));
        // Every hop trusted: the first.
        Assert.Equal(("10.0.0.1", "http"), Resolve(proxies, "127.0.0.1", ("X-Forwarded-For", "10.0.0.1, 10.0.0.2")));
        // No forwarding header: the proxy itself.
        Assert.Equal(("127.0.0.1", "http"), Resolve(proxies, "127.0.0.1"));
        // An IPv4 address mapped into IPv6 is IPv4's.
        Assert.Equal(("203.0.113.7", "http"), Resolve(proxies, "::ffff:127.0.0.1", ("X-Forwarded-For", "203.0.113.7")));
        Assert.Equal(("127.0.0.2", "http"), Resolve(Proxies("::1"), "::ffff:127.0.0.2", ("X-Forwarded-For", "203.0.113.7")));
    }

    [Fact]
    public void ForwardedIsReadBeforeTheOthers_ItsAddressesWithPortsAndBrackets()
    {
        var proxies = Proxies("127.0.0.1", "::1");
        Assert.Equal(("192.0.2.60", "https"), Resolve(proxies, "127.0.0.1", ("Forwarded", "for=192.0.2.60;proto=https;by=203.0.113.43"),
            ("X-Forwarded-For", "198.51.100.1")));
        Assert.Equal(("2001:db8:cafe::17", "http"), Resolve(proxies, "::1", ("Forwarded", "For=\"[2001:db8:cafe::17]:4711\"")));
        Assert.Equal(("192.0.2.43", "https"), Resolve(proxies, "127.0.0.1", ("Forwarded", "for=192.0.2.43:47011;proto=HTTPS, for=127.0.0.1")));
        Assert.Equal(("198.51.100.17", "http"), Resolve(proxies, "127.0.0.1", ("Forwarded", "for=\"198.51.100.17;quoted\", for=198.51.100.17"),
            ("Forwarded", "for=127.0.0.1;proto=ftp")));
        // An address it does not give, "unknown" or obfuscated: the proxy's own.
        Assert.Equal(("127.0.0.1", "https"), Resolve(proxies, "127.0.0.1", ("Forwarded", "for=unknown;proto=https")));
        Assert.Equal(("127.0.0.1", "http"), Resolve(proxies, "127.0.0.1", ("Forwarded", "for=_hidden")));
    }
}
