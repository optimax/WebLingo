using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Crypto;

/// <summary>
/// <c>std:crypto</c>, a standard module (specification, section 7.10, M0.18), a native module installed with the runtime in
/// the <c>std</c> scheme's folder as <c>crypto.dll</c>, through the platform's cryptography (<see cref="WebLingoHeap.Crypto"/>),
/// so that the portable core holds none of its own (the owner's answers, 17 November 2026): <c>randomBytes(n)</c>, secure
/// random bytes; <c>sha256(data)</c> and <c>sha512(data)</c>; <c>hmac(algorithm, key, data)</c>, <c>"sha256"</c> or
/// <c>"sha512"</c>; <c>equal(a, b)</c>, in constant time; and passwords' hashes, PBKDF2 with HMAC-SHA-256, 600,000
/// iterations and a 16-byte random salt, as OWASP advises, written <c>pbkdf2-sha256$600000$salt$hash</c>, the salt and the
/// hash in Base64, so that a later default still verifies an older hash: <c>hashPassword(password)</c> and
/// <c>verifyPassword(password, hash)</c>, each blocking its worker, and <c>hashPasswordAsync</c> and
/// <c>verifyPasswordAsync</c>, each a task its work, run off the worker, completes. Data, a key and a password are a
/// string, as UTF-8, or <c>Bytes</c>; a hash is <c>Bytes</c>.
/// </summary>
public sealed class WebLingoCryptoModule : IWebLingoNativeModule
{
    /// <summary>The iterations a new password's hash takes, OWASP's advice for PBKDF2 with HMAC-SHA-256.</summary>
    public const int Iterations = 600_000;
    /// <summary>The most iterations a hash read may name, so that a hash from outside cannot hold a worker for ever.</summary>
    public const int MostIterations = 10_000_000;
    /// <summary>The most random bytes one call gives.</summary>
    public const int MostRandomBytes = 1024 * 1024;

    private const string Scheme = "pbkdf2-sha256";
    private const int SaltLength = 16;
    private const int HashLength = 32;

    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Function("randomBytes", 1, RandomBytes);
        registry.Function("sha256", 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            Hash("sha256", a, heap, out result, out error));
        registry.Function("sha512", 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            Hash("sha512", a, heap, out result, out error));
        registry.Function("hmac", 3, Hmac);
        registry.Function("equal", 2, Equal);
        registry.Function("hashPassword", 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            HashPassword(true, a, heap, out result, out error));
        registry.Function("hashPasswordAsync", 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            HashPassword(false, a, heap, out result, out error));
        registry.Function("verifyPassword", 2, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            VerifyPassword(true, a, heap, out result, out error));
        registry.Function("verifyPasswordAsync", 2, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            VerifyPassword(false, a, heap, out result, out error));
    }

    private static bool RandomBytes(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryCrypto(heap, out var crypto, out error)) return false;
        if (a[0].Kind != WebLingoValueKind.Int64 || a[0].AsInt64 is < 0 or > MostRandomBytes)
        {
            error = Invalid($"'randomBytes' gives 0 to {MostRandomBytes} bytes, not {WebLingoValueFormatter.Describe(a[0], heap)}.");
            return false;
        }
        var bytes = new byte[a[0].AsInt64];
        crypto.FillRandom(bytes);
        return heap.TryAllocateBytes(bytes, out result, out error);
    }

    private static bool Hash(string algorithm, ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryCrypto(heap, out var crypto, out error) || !TryData(a[0], algorithm, "data", heap, out var data, out error)) return false;
        return heap.TryAllocateBytes(crypto.Hash(algorithm, data), out result, out error);
    }

    private static bool Hmac(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryCrypto(heap, out var crypto, out error)) return false;
        if (a[0].Kind != WebLingoValueKind.String || heap.GetString(a[0]) is not ("sha256" or "sha512"))
        {
            var given = a[0].Kind == WebLingoValueKind.String ? $"\"{heap.GetString(a[0])}\"" : WebLingoValueFormatter.Describe(a[0], heap);
            error = Invalid($"'hmac' hashes by \"sha256\" or \"sha512\", not {given}.");
            return false;
        }
        if (!TryData(a[1], "hmac", "key", heap, out var key, out error) || !TryData(a[2], "hmac", "data", heap, out var data, out error)) return false;
        return heap.TryAllocateBytes(crypto.Hmac(heap.GetString(a[0]), key, data), out result, out error);
    }

    private static bool Equal(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryCrypto(heap, out var crypto, out error) || !TryData(a[0], "equal", "first value", heap, out var first, out error)
            || !TryData(a[1], "equal", "second value", heap, out var second, out error))
        {
            return false;
        }
        result = WebLingoValue.FromBoolean(crypto.FixedTimeEquals(first, second));
        return true;
    }

    /// <summary>
    /// <c>hashPassword(password)</c>: its hash, with a new random salt, written <c>pbkdf2-sha256$600000$salt$hash</c>; at once,
    /// blocking the worker, or as a task, its work run off the worker.
    /// </summary>
    private static bool HashPassword(bool sync, ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var name = sync ? "hashPassword" : "hashPasswordAsync";
        if (!TryCrypto(heap, out var crypto, out error) || !TryData(a[0], name, "password", heap, out var password, out error)) return false;
        var salt = new byte[SaltLength];
        crypto.FillRandom(salt);
        return Run(sync, heap, () => Scheme + "$" + Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture) + "$" + Base64(salt) + "$"
                + Base64(crypto.Pbkdf2Sha256(password, salt, Iterations, HashLength)),
            (string text, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? wrong) => h.TryAllocateString(text, out made, out wrong),
            out result, out error);
    }

    /// <summary>
    /// <c>verifyPassword(password, hash)</c>: whether the password is the one <c>hashPassword</c> made the hash of, by the hash's
    /// own iterations and salt, compared in constant time; WL3001 for a hash that is not one it makes.
    /// </summary>
    private static bool VerifyPassword(bool sync, ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var name = sync ? "verifyPassword" : "verifyPasswordAsync";
        if (!TryCrypto(heap, out var crypto, out error) || !TryData(a[0], name, "password", heap, out var password, out error)) return false;
        if (a[1].Kind != WebLingoValueKind.String || !TryParse(heap.GetString(a[1]), out var iterations, out var salt, out var expected))
        {
            // A string's text is not repeated: a hash, even one wrongly written, is kept out of messages and logs.
            error = Invalid($"'{name}' takes a hash 'hashPassword' made, \"{Scheme}$iterations$salt$hash\""
                + (a[1].Kind == WebLingoValueKind.String ? ", which this string is not." : $", not {WebLingoValueFormatter.Describe(a[1], heap)}."));
            return false;
        }
        return Run(sync, heap, () => crypto.FixedTimeEquals(crypto.Pbkdf2Sha256(password, salt, iterations, expected.Length), expected),
            (bool same, WebLingoHeap _, out WebLingoValue made, out WebLingoRuntimeError? wrong) =>
            {
                (made, wrong) = (WebLingoValue.FromBoolean(same), null);
                return true;
            }, out result, out error);
    }

    /// <summary><c>pbkdf2-sha256$iterations$salt$hash</c>, read: iterations from 1 to <see cref="MostIterations"/>, a salt and a hash in Base64.</summary>
    private static bool TryParse(string text, out int iterations, out byte[] salt, out byte[] hash)
    {
        (iterations, salt, hash) = (0, [], []);
        var parts = text.Split('$');
        return parts.Length == 4 && parts[0] == Scheme
            && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out iterations)
            && iterations is >= 1 and <= MostIterations
            && TryFromBase64(parts[2], out salt) && TryFromBase64(parts[3], out hash) && hash.Length > 0;
    }

    /// <summary>
    /// Runs the work: at once, blocking, for a synchronous form, its result made a value; or off the worker, a task the
    /// result completes.
    /// </summary>
    private static bool Run<T>(bool sync, WebLingoHeap heap, Func<T> work, WebLingoOffWorkerFinish<T> finish, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (sync) return finish(work(), heap, out result, out error);
        if (heap.Scheduler is not { } scheduler)
        {
            error = Invalid("std:crypto's asynchronous functions need a VM that runs tasks.");
            return false;
        }
        return scheduler.TryRunOffWorker(work, finish, _ => null, out result, out error);
    }

    private static bool TryCrypto(WebLingoHeap heap, out IWebLingoCrypto crypto, out WebLingoRuntimeError? error)
    {
        (crypto, error) = (heap.Crypto!, null);
        if (heap.Crypto is not null) return true;
        error = Invalid("This host gives no cryptography.");
        return false;
    }

    /// <summary>Data as bytes: a string's UTF-8, or bytes as they are; WL3001 for anything else.</summary>
    private static bool TryData(WebLingoValue value, string function, string what, WebLingoHeap heap, out byte[] data, out WebLingoRuntimeError? error)
    {
        (data, error) = ([], null);
        if (value.Kind == WebLingoValueKind.String)
        {
            data = WebLingoTextCodec.Encode(heap.GetString(value), WebLingoCharset.Utf8);
            return true;
        }
        if (value.Kind == WebLingoValueKind.Bytes)
        {
            data = heap.GetBytes(value);
            return true;
        }
        error = Invalid($"'{function}' takes its {what} as a string or bytes, not {WebLingoValueFormatter.Describe(value, heap)}.");
        return false;
    }

    private static WebLingoRuntimeError Invalid(string message) => new(WebLingoDiagnosticCodes.InvalidOperation, message);

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    /// <summary>Bytes in Base64, RFC 4648's, with its padding, which a password's hash writes its salt and its hash in.</summary>
    private static string Base64(byte[] bytes)
    {
        var text = new System.Text.StringBuilder((bytes.Length + 2) / 3 * 4);
        for (var i = 0; i < bytes.Length; i += 3)
        {
            var count = Math.Min(3, bytes.Length - i);
            var chunk = bytes[i] << 16 | (count > 1 ? bytes[i + 1] << 8 : 0) | (count > 2 ? bytes[i + 2] : 0);
            for (var j = 0; j < 4; j++) text.Append(j <= count ? Alphabet[(chunk >> (18 - 6 * j)) & 63] : '=');
        }
        return text.ToString();
    }

    private static bool TryFromBase64(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length == 0 || text.Length % 4 != 0) return false;
        var padding = text.EndsWith("==", StringComparison.Ordinal) ? 2 : text.EndsWith('=') ? 1 : 0;
        var made = new byte[text.Length / 4 * 3 - padding];
        var at = 0;
        for (var i = 0; i < text.Length; i += 4)
        {
            var chunk = 0;
            for (var j = 0; j < 4; j++)
            {
                var c = text[i + j];
                var value = c == '=' && i + 4 == text.Length && j >= 4 - padding ? 0 : Alphabet.IndexOf(c);
                if (value < 0) return false;
                chunk = chunk << 6 | value;
            }
            for (var j = 0; j < 3 && at < made.Length; j++) made[at++] = (byte)(chunk >> (16 - 8 * j));
        }
        bytes = made;
        return true;
    }
}
