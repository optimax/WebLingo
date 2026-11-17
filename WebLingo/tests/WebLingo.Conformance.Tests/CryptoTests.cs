using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:crypto</c> (specification, section 7.10; section 9, M0.18), through <c>weblingo</c>, each program run as a script,
/// on the register VM, and as a <c>.wlbc</c> compiled for the stack VM, which must give the same: published test vectors for
/// SHA-256, SHA-512, HMAC (RFC 4231) and PBKDF2 with HMAC-SHA-256, which <c>verifyPassword</c> reads; random bytes; a
/// comparison in constant time; passwords' hashes, synchronously and as tasks; and the errors.
/// </summary>
public sealed class CryptoTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-crypto-" + Guid.NewGuid().ToString("N"));

    private const string Hex = "import crypto from \"std:crypto\"; function hex(b) { var s = \"\"; for (var i = 0; i < b.length; i++) s += $\"{b[i]:x2}\"; return s; } ";

    public CryptoTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(Hex + "writeLn(hex(crypto.sha256(\"abc\")));", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData(Hex + "writeLn(hex(crypto.sha256(Bytes.from(\"abc\"))) == hex(crypto.sha256(\"abc\")));", "true")]
    [InlineData(Hex + "writeLn(hex(crypto.sha512(\"\")).slice(0, 32));", "cf83e1357eefb8bdf1542850d66d8007")]
    [InlineData(Hex + "writeLn(hex(crypto.hmac(\"sha256\", \"Jefe\", \"what do ya want for nothing?\")));",
        "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843")]
    [InlineData(Hex + "writeLn(hex(crypto.hmac(\"sha512\", \"Jefe\", \"what do ya want for nothing?\")).slice(0, 32));",
        "164b7a7bfcf819e2e395fbe73b56e0a3")]
    [InlineData(Hex + "var r = crypto.randomBytes(16); writeLn([r.length, r == crypto.randomBytes(16), crypto.randomBytes(0).length, r is Bytes]);",
        "[16, false, 0, true]")]
    [InlineData(Hex + "writeLn([crypto.equal(\"a\", Bytes.from(\"a\")), crypto.equal(\"a\", \"b\"), crypto.equal(Bytes.from(\"ab\"), \"a\"), crypto.equal(\"\", \"\")]);",
        "[true, false, false, true]")]
    // PBKDF2 with HMAC-SHA-256's published vectors (RFC 7914's and others'), read as hashes hashPassword could have made.
    [InlineData(Hex + "writeLn([crypto.verifyPassword(\"password\", \"pbkdf2-sha256$1$c2FsdA==$Eg+2z/z4syxD5yJSVsT4N6hlSMkszDVICAWYfLcL4Xs=\"), " +
                      "crypto.verifyPassword(Bytes.from(\"password\"), \"pbkdf2-sha256$4096$c2FsdA==$xeR41ZKIyEGqUw22hFxMjZYok6ABzk4RpJY4c6qYE0o=\"), " +
                      "crypto.verifyPassword(\"Password\", \"pbkdf2-sha256$1$c2FsdA==$Eg+2z/z4syxD5yJSVsT4N6hlSMkszDVICAWYfLcL4Xs=\")]);",
        "[true, true, false]")]
    [InlineData(Hex + "var h = crypto.hashPassword(\"correct horse\"); var p = h.split(\"$\"); " +
                      "writeLn([p[0], p[1], p.length, Bytes.from(p[2]).length, crypto.verifyPassword(\"correct horse\", h), crypto.verifyPassword(\"wrong\", h), " +
                      "h == crypto.hashPassword(\"correct horse\")]);",
        "[\"pbkdf2-sha256\", \"600000\", 4, 24, true, false, false]")]
    [InlineData(Hex + "var t = crypto.hashPasswordAsync(\"s3cret\"); writeLn(t is Task); var h = await t; " +
                      "writeLn([await crypto.verifyPasswordAsync(\"s3cret\", h), await crypto.verifyPasswordAsync(\"nope\", h)]);",
        "true\n[true, false]")]
    public void ResultsAreTheSpecifiedOnes(string program, string printed)
    {
        Assert.Equal((printed + "\n", ""), Run(program));
    }

    [Theory]
    [InlineData("crypto.hmac(\"md5\", \"k\", \"d\");", "'hmac' hashes by \"sha256\" or \"sha512\", not \"md5\".")]
    [InlineData("crypto.randomBytes(-1);", "'randomBytes' gives 0 to 1048576 bytes, not the integer -1.")]
    [InlineData("crypto.randomBytes(1048577);", "'randomBytes' gives 0 to 1048576 bytes, not the integer 1048577.")]
    [InlineData("crypto.sha256(5);", "'sha256' takes its data as a string or bytes, not the integer 5.")]
    [InlineData("crypto.hashPassword(null);", "'hashPassword' takes its password as a string or bytes, not null.")]
    [InlineData("crypto.verifyPassword(\"p\", \"md5$x\");",
        "'verifyPassword' takes a hash 'hashPassword' made, \"pbkdf2-sha256$iterations$salt$hash\", which this string is not.")]
    [InlineData("crypto.verifyPassword(\"p\", \"pbkdf2-sha256$0$c2FsdA==$c2FsdA==\");",
        "'verifyPassword' takes a hash 'hashPassword' made, \"pbkdf2-sha256$iterations$salt$hash\", which this string is not.")]
    [InlineData("crypto.verifyPassword(\"p\", \"pbkdf2-sha256$99999999$c2FsdA==$c2FsdA==\");",
        "'verifyPassword' takes a hash 'hashPassword' made, \"pbkdf2-sha256$iterations$salt$hash\", which this string is not.")]
    [InlineData("crypto.verifyPassword(\"p\", 5);", "'verifyPassword' takes a hash 'hashPassword' made, \"pbkdf2-sha256$iterations$salt$hash\", not the integer 5.")]
    public void WrongArgumentsAreTypeErrors(string call, string message)
    {
        Assert.Equal(("WL3001 " + message + "\n", ""), Run("import crypto from \"std:crypto\"; try { " + call + " } catch (TypeError e) { writeLn(e.code + \" \" + e.message); }"));
    }

    /// <summary>
    /// The program run as a script, on the register VM, then compiled for the stack VM and run from its <c>.wlbc</c>: what
    /// both write, which must be the same, and their errors.
    /// </summary>
    private (string Output, string Error) Run(string program)
    {
        var path = Path.Combine(_folder, "program.wl");
        File.WriteAllText(path, program);
        var register = Weblingo("run", path);
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", "--stack", path).Exit);
        var stack = Weblingo("run", Path.ChangeExtension(path, ".wlbc"));
        Assert.Equal((register.Output, register.Error), (stack.Output, stack.Error));
        return (register.Output, register.Error);
    }

    private static (int Exit, string Output, string Error) Weblingo(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
