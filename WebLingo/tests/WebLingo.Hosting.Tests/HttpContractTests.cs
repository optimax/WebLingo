using System.Text;
using WebLingo.Hosting.Abstractions;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// WLHI's HTTP contracts (specification, section 6.7; section 9, "M0.14 parts", part 1), through the buffered request and
/// response: headers and their rules, a response's states, and bodies read and written in pieces.
/// </summary>
public sealed class HttpContractTests
{
    [Fact]
    public void HeadersAreNamedWithoutRegardToCaseAndKeepTheirValuesInOrder()
    {
        var headers = new WebLingoHeaderList();
        Assert.Equal(WebLingoHeaderChange.Done, headers.Add("Accept", "text/html"));
        Assert.Equal(WebLingoHeaderChange.Done, headers.Add("accept", "application/json"));
        Assert.Equal(WebLingoHeaderChange.Done, headers.Set("X-Count", "1"));
        Assert.Equal(["Accept", "X-Count"], headers.Names);
        Assert.Equal(["text/html", "application/json"], headers.GetValues("ACCEPT"));
        Assert.Equal("text/html, application/json", headers.Get("Accept"));

        Assert.Equal(WebLingoHeaderChange.Done, headers.Set("x-count", "2"));
        Assert.Equal(["2"], headers.GetValues("X-Count"));
        Assert.Equal(WebLingoHeaderChange.Done, headers.Remove("ACCEPT"));
        Assert.Equal(WebLingoHeaderChange.Done, headers.Remove("Missing"));
        Assert.Equal(["X-Count"], headers.Names);
        Assert.Empty(headers.GetValues("Accept"));
        Assert.Null(headers.Get("Accept"));
    }

    [Theory]
    [InlineData("", "v", WebLingoHeaderChange.InvalidName)]
    [InlineData("Bad Name", "v", WebLingoHeaderChange.InvalidName)]
    [InlineData("Bad:Name", "v", WebLingoHeaderChange.InvalidName)]
    [InlineData("Set-Cookie", "a=1\r\nX-Injected: yes", WebLingoHeaderChange.InvalidValue)]
    [InlineData("X-Line", "a\nb", WebLingoHeaderChange.InvalidValue)]
    [InlineData("X-Nul", "a\0b", WebLingoHeaderChange.InvalidValue)]
    [InlineData("X-Text", "café", WebLingoHeaderChange.InvalidValue)]
    [InlineData("X-Ok", "a\tb ~!", WebLingoHeaderChange.Done)]
    [InlineData("x-ok_1.2!#$%&'*+^`|~", "", WebLingoHeaderChange.Done)]
    public void AHeaderMustBeAToken_ItsValueVisibleAscii(string name, string value, WebLingoHeaderChange change)
    {
        var headers = new WebLingoHeaderList();
        Assert.Equal(change, headers.Set(name, value));
        Assert.Equal(change, headers.Add(name, value));
        Assert.Equal(change == WebLingoHeaderChange.Done ? 1 : 0, headers.Names.Count);
    }

    [Fact]
    public void ARequestKeepsWhatItWasGiven_ItsHeadersFixed()
    {
        var request = new WebLingoBufferedRequest("POST", "/products/7", "sort=name&page=2", "https", "example.com:8080",
            [("Content-Type", "text/plain"), ("Bad Name", "dropped"), ("X-Tag", "a"), ("x-tag", "b")]);
        Assert.Equal(("POST", "/products/7", "sort=name&page=2", "https", "example.com:8080"),
            (request.Method, request.Path, request.QueryString, request.Scheme, request.Host));
        Assert.True(request.Headers.IsReadOnly);
        Assert.Equal(["Content-Type", "X-Tag"], request.Headers.Names);
        Assert.Equal("a, b", request.Headers.Get("X-TAG"));
        Assert.Equal(WebLingoHeaderChange.ReadOnly, request.Headers.Set("X-Tag", "c"));
        Assert.Equal(WebLingoHeaderChange.ReadOnly, request.Headers.Remove("X-Tag"));
        Assert.Equal(0, request.Body.Read(new byte[4], 0, 4));
    }

    [Fact]
    public void ARequestsBodyIsReadInPieces()
    {
        var request = new WebLingoBufferedRequest("POST", "/", body: Encoding.UTF8.GetBytes("hello, world"));
        var buffer = new byte[8];
        Assert.Equal(5, request.Body.Read(buffer, 1, 5));
        Assert.Equal("hello", Encoding.UTF8.GetString(buffer, 1, 5));
        Assert.Equal(0, request.Body.Read(buffer, 5, 4));
        Assert.Equal(0, request.Body.Read(buffer, -1, 2));
        Assert.Equal(7, request.Body.Read(buffer, 0, 8));
        Assert.Equal(", world", Encoding.UTF8.GetString(buffer, 0, 7));
        Assert.Equal(0, request.Body.Read(buffer, 0, 8));
    }

    [Fact]
    public void AResponseIsUncommittedUntilItsBodyIsWritten_ThenItsStatusAndHeadersAreFixed()
    {
        var response = new WebLingoBufferedResponse();
        Assert.Equal((WebLingoResponseState.Uncommitted, 200), (response.State, response.StatusCode));
        Assert.True(response.TrySetStatusCode(404));
        Assert.False(response.TrySetStatusCode(99));
        Assert.False(response.TrySetStatusCode(1000));
        Assert.Equal(WebLingoHeaderChange.Done, response.Headers.Set("Content-Type", "text/plain; charset=utf-8"));

        var text = Encoding.UTF8.GetBytes("Not here.");
        Assert.True(response.Body.Write(text, 0, 4));
        Assert.Equal((WebLingoResponseState.Committed, 404), (response.State, response.StatusCode));
        Assert.False(response.TrySetStatusCode(200));
        Assert.Equal(404, response.StatusCode);
        Assert.True(response.Headers.IsReadOnly);
        Assert.Equal(WebLingoHeaderChange.ReadOnly, response.Headers.Set("X-Late", "1"));
        Assert.True(response.Body.Write(text, 4, 5));
        Assert.False(response.Body.Write(text, 5, 5));

        response.Complete();
        Assert.Equal(WebLingoResponseState.Completed, response.State);
        Assert.False(response.Body.Write(text, 0, 1));
        Assert.Equal("Not here.", Encoding.UTF8.GetString(response.BodyBytes));
        Assert.Equal("text/plain; charset=utf-8", response.Headers.Get("content-type"));
    }

    [Fact]
    public void CommittingAndCompletingAreEachDoneOnce()
    {
        var response = new WebLingoBufferedResponse();
        response.Commit();
        Assert.Equal(WebLingoResponseState.Committed, response.State);
        Assert.False(response.TrySetStatusCode(500));
        response.Commit();
        Assert.Equal(WebLingoResponseState.Committed, response.State);

        // Completing an uncommitted response commits it, its body empty.
        var empty = new WebLingoBufferedResponse();
        empty.Complete();
        empty.Complete();
        Assert.Equal((WebLingoResponseState.Completed, 200, 0), (empty.State, empty.StatusCode, empty.BodyBytes.Length));
        Assert.True(empty.Headers.IsReadOnly);
    }

    [Fact]
    public void ALargeBodyIsKeptWhole()
    {
        var response = new WebLingoBufferedResponse();
        var piece = new byte[1000];
        for (var i = 0; i < piece.Length; i++) piece[i] = (byte)i;
        for (var i = 0; i < 100; i++) Assert.True(response.Body.Write(piece, 0, piece.Length));
        var body = response.BodyBytes;
        Assert.Equal(100_000, body.Length);
        Assert.Equal(piece, body[99_000..]);
    }
}
