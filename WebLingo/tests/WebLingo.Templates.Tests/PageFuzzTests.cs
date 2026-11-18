using System.Text;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Language;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// Random pages, with two includes, a component and a layout, made of pieces of HTML and of code (specification, section
/// 13): each gives diagnostics or runs, never crashes, alike on both VMs, every diagnostic and runtime error within the file
/// it names. Positions cross files here, as a fuzz test of one file never makes them do: an include's interpolation's
/// format, read from the page's text by its span, crashed the compiler until 17 November 2026.
/// </summary>
public sealed class PageFuzzTests
{
    private static readonly string[] Html =
    [
        "<p>", "</p>", "\n", "\r\n", "<a href=\"", "\">", "<script>", "</script>", "<style>", "</style>", "x", "é", "\U0001F600", " ",
        "<!-- ", " -->", "'", "\"", "<p>a line of text, longer than most of the pieces of code</p>",
    ];

    private static readonly string[] Code =
    [
        "$\"{1:0.00}\"", "$\"{x}\"", "$\"{{\"", "$\"a{1,5}b\"", "$\"{1,-3:0}\"", "$\"{1:N99999}\"", "$\"{\"", "$\"{1:}\"", "$\"{:x}\"",
        "\"\"\"\n  a\n  \"\"\"", "$\"\"\"{1}\"\"\"", "$$\"\"\"{{1}}\"\"\"", "\"\"\"", "$\"{$\"{1:0}\"}\"", "$\"{DateTime.utc(2026, 1, 1):yyyy}\"",
        "x", "var x = 1;", "include \"/h.wasp\";", "include \"/i.wasp\";", "await renderComponent(\"/c.wasp\", {});",
        "renderComponent(\"/c.wasp\");", "if (true) {", "}", "{", "foreach (var i in [1, 2]) {", "[1][5]", "y", "\"s\"", "'%>'", "\"%>\"",
        "// c", "/* c */", "1 +", ";", "(", ")", "x?[0]", "{ a: 1 }", "#{1}", "=> 1", "class C {}", "new C()", "layout \"/l.wasp\", {};",
        "renderBody();", "param p = 1;", "1", "null", "import { write, writeLn } from \"std:io\"; write(1);", "else {", "} else {", "while (false) {", "switch (1) { case 1 { } }",
        "try {", "} catch (e) {", "throw 1;", "async function f() {", "await 1;", "return;", "x = 2;", "...[1]", "f(", "%", "?", ":",
        "\\", "@", "\"\\u{110000}\"", "0x", "1_", "1.5m", "Infinity", "-", "!", "&&", "[", "]", ",", ".", "?.", "??", "=",
        "[].iterate().next()", "new BytesBuilder().add(true)", "class Q extends Integer {} new Q(\"x\")", "[1, 2].map((v) => [v][3])",
    ];

    private static readonly string[] Files = ["page.wasp", "h.wasp", "i.wasp", "c.wasp", "l.wasp"];

    private static readonly WebLingoCompilerOptions Options =
        new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1), new WebLingoBuiltinDeclaration("delay", 1)]);

    [Fact]
    public void RandomPagesGiveDiagnosticsOrRunAndNeverCrash()
    {
        const int Rounds = 10_000;
        string? failure = null;
        var compiled = 0;
        // On a thread with the compiler's stack, as the hosts compile (Documents/implementation.md, "The Compiler Needs a
        // Stack of Its Own").
        var thread = new Thread(() =>
        {
            var random = new Random(13_17);
            for (var round = 0; round < Rounds && failure is null; round++)
            {
                var texts = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var name in Files) texts[name] = Template(random);
                var templates = new StringBuilder();
                foreach (var (name, text) in texts)
                {
                    if (name != "page.wasp") templates.Append(name).Append('=').Append(text).Append('|');
                }
                try
                {
                    if (CompilesWithinItsFiles(texts["page.wasp"], templates.ToString())) compiled++;
                    PageTests.Render(texts["page.wasp"], PageTests.Templates(templates.ToString()));
                }
                catch (Exception exception)
                {
                    failure = $"{exception.GetType().Name}: {exception.Message}\npage.wasp={texts["page.wasp"]}\n{templates}";
                }
            }
        }, WebLingoParser.CompilerStackSize);
        thread.Start();
        thread.Join();
        Assert.True(failure is null, failure);
        Assert.InRange(compiled, Rounds / 20, Rounds - 1);
    }

    /// <summary>A file's text: pieces of HTML and blocks of pieces of code, a block's <c>%&gt;</c> sometimes left out.</summary>
    private static string Template(Random random)
    {
        var text = new StringBuilder();
        for (var n = random.Next(0, 7); n > 0; n--)
        {
            if (random.Next(3) == 0)
            {
                text.Append(Html[random.Next(Html.Length)]);
                continue;
            }
            text.Append(random.Next(10) switch { 0 => "<%- ", 1 => "<%# ", 2 or 3 or 4 => "<%= ", _ => "<% " });
            for (var k = random.Next(1, 5); k > 0; k--) text.Append(Code[random.Next(Code.Length)]).Append(' ');
            if (random.Next(15) != 0) text.Append("%>");
        }
        return text.ToString();
    }

    /// <summary>
    /// The page compiled, and run on the register VM, its diagnostics and its runtime error each within the text of the file
    /// it names (an exception if one is not); whether it compiled.
    /// </summary>
    private static bool CompilesWithinItsFiles(string pageText, string templates)
    {
        var page = new WebLingoSourceText(pageText, "page.wasp");
        var compiler = new WebLingoTemplateCompiler(Options, PageTests.Templates(templates));
        var result = compiler.Compile(page);
        foreach (var diagnostic in result.Diagnostics) WithinItsFile(compiler.Sources, page, diagnostic);
        if (result.Module is null) return false;
        var output = new WebLingoCollectedOutput();
        var services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output));
        foreach (var builtin in WebLingoTemplateBuiltins.Create(output)) services.Register(builtin);
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0)) { Services = services, InstructionBudget = 100_000 };
        var run = new WebLingoVirtualMachine { Components = compiler.Components }.Execute(result.Module, context);
        if (run.Error is { } error) WithinItsFile(compiler.Sources, page, compiler.Sources.Locate(error));
        return true;
    }

    private static void WithinItsFile(WebLingoTemplateSources sources, WebLingoSourceText page, WebLingoDiagnostic diagnostic)
    {
        var text = diagnostic.Source is { } name ? sources.TextOf(name) : page;
        if (text is null || diagnostic.Span.Start < 0 || diagnostic.Span.Start > text.Text.Length)
        {
            throw new InvalidOperationException(
                $"{diagnostic.Code} is not within the file it names, {diagnostic.Source ?? page.Name}, at {diagnostic.Span}: {diagnostic.Message}");
        }
    }
}
