namespace WebLingo.Platform.Abstractions;

/// <summary>
/// The platform's cryptography (specification, section 7.10, WL-P010; the conceptual <c>IWebLingoCryptoService</c> of
/// section 7.3), which <c>std:crypto</c> reaches, so that the portable core holds none of its own: secure random bytes,
/// SHA-256 and SHA-512, HMAC with either, PBKDF2 with HMAC-SHA-256, and a comparison in constant time. An algorithm is
/// named in lower case, <c>sha256</c> or <c>sha512</c>. Safe on several threads at once.
/// </summary>
public interface IWebLingoCrypto
{
    /// <summary>Fills <paramref name="bytes"/> with secure random bytes.</summary>
    void FillRandom(Span<byte> bytes);

    /// <summary>The hash of <paramref name="data"/> by <paramref name="algorithm"/>, <c>sha256</c> or <c>sha512</c>.</summary>
    byte[] Hash(string algorithm, ReadOnlySpan<byte> data);

    /// <summary>The HMAC of <paramref name="data"/> with <paramref name="key"/>, by <paramref name="algorithm"/>, <c>sha256</c> or <c>sha512</c>.</summary>
    byte[] Hmac(string algorithm, ReadOnlySpan<byte> key, ReadOnlySpan<byte> data);

    /// <summary>PBKDF2 with HMAC-SHA-256: <paramref name="length"/> bytes derived from a password and a salt, in <paramref name="iterations"/>.</summary>
    byte[] Pbkdf2Sha256(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int iterations, int length);

    /// <summary>Whether two byte sequences are equal, in a time that depends on their lengths only, never on where they differ.</summary>
    bool FixedTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b);
}
