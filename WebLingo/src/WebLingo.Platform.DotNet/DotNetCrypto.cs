using System.Security.Cryptography;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// The platform's cryptography on .NET (specification, section 7.10): <see cref="RandomNumberGenerator"/>,
/// <see cref="SHA256"/> and <see cref="SHA512"/>, <see cref="HMACSHA256"/> and <see cref="HMACSHA512"/>,
/// <see cref="Rfc2898DeriveBytes.Pbkdf2(ReadOnlySpan{byte}, ReadOnlySpan{byte}, int, HashAlgorithmName, int)"/> and
/// <see cref="CryptographicOperations.FixedTimeEquals"/>, each one-shot and so safe on several threads at once. Adapter-only
/// (section 7.14).
/// </summary>
public sealed class DotNetCrypto : IWebLingoCrypto
{
    public void FillRandom(Span<byte> bytes) => RandomNumberGenerator.Fill(bytes);

    public byte[] Hash(string algorithm, ReadOnlySpan<byte> data) => algorithm switch
    {
        "sha256" => SHA256.HashData(data),
        "sha512" => SHA512.HashData(data),
        _ => throw new ArgumentException($"'{algorithm}' is not a hash this platform has.", nameof(algorithm))
    };

    public byte[] Hmac(string algorithm, ReadOnlySpan<byte> key, ReadOnlySpan<byte> data) => algorithm switch
    {
        "sha256" => HMACSHA256.HashData(key, data),
        "sha512" => HMACSHA512.HashData(key, data),
        _ => throw new ArgumentException($"'{algorithm}' is not a hash this platform has.", nameof(algorithm))
    };

    public byte[] Pbkdf2Sha256(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int iterations, int length) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length);

    public bool FixedTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => CryptographicOperations.FixedTimeEquals(a, b);
}
