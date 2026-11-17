using WebLingo.Compiler;
using Xunit;
namespace WebLingo.Compiler.Tests;

/// <summary>
/// A native class's methods written in WebLingo (specification, section 3.9), read when its module loads: methods alone,
/// as a class's body declares them, each a name no other member has.
/// </summary>
public sealed class NativeClassMethodsTests
{
    [Fact]
    public void MethodsAreRead()
    {
        var taken = new HashSet<string> { "begin" };
        Assert.True(WebLingoNativeClassInfo.TryParseMethods("async run(work) { await this.begin(); return await work(); }\nstatic make() { return 1; }",
            taken, out var methods, out var error), error);
        Assert.Equal(["run", "make"], methods.Select(method => method.Name));
        Assert.True(methods[0].IsAsync);
        Assert.True(methods[1].IsStatic);
    }

    [Theory]
    [InlineData("run( { }", "its methods written in WebLingo do not parse:")]
    [InlineData("static var count = 0;", "its methods written in WebLingo are not methods alone")]
    [InlineData("begin() { }", "its method written in WebLingo 'begin' has a name another member has")]
    [InlineData("run() { } run() { }", "its method written in WebLingo 'run' has a name another member has")]
    [InlineData("constructor() { }", "its method written in WebLingo 'constructor' has a name another member has")]
    public void WhatIsNotMethodsIsRefused(string source, string message)
    {
        Assert.False(WebLingoNativeClassInfo.TryParseMethods(source, new HashSet<string> { "begin" }, out _, out var error));
        Assert.StartsWith(message, error);
    }
}
