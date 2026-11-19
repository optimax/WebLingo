using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

public sealed class UtilityModuleTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-utilities-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PathsRecognizeEitherSeparatorAndEmitTheHostSeparator(bool stack)
    {
        var separator = Path.DirectorySeparatorChar;
        Run(stack, """
            import path from "std:path";
            writeLn(path.join("base\\dir", "sub/file.txt"));
            writeLn(path.join("ignored", "C:\\folder", "file"));
            writeLn(path.dirname("\\\\server/share/folder.txt"));
            writeLn(path.basename("C:report.txt"));
            writeLn(path.extension("folder\\report.txt"));
            writeLn(path.extension(".profile"));
            try { path.join("x", 4); } catch (TypeError e) { writeLn(e.code + " " + e.message); }
            """,
            $"base{separator}dir{separator}sub{separator}file.txt\nC:{separator}folder{separator}file\n"
            + $"{separator}{separator}server{separator}share\nreport.txt\n.txt\n\n"
            + "WL3001 The second argument of 'join', the path part, must be a path string without NUL, not the integer 4.\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UrlsPreserveDuplicateQueryKeysAndResolveRelativeReferences(bool stack)
    {
        Run(stack, """
            import url from "std:url";
            var parts = url.parse("https://example.test:8443/a?q=tea#top");
            writeLn([parts["scheme"], parts["host"], parts["port"], parts["path"], parts["query"], parts["fragment"]]);
            writeLn(url.resolve("https://example.test/a/b", "../c?q=tea"));
            var pairs = url.parseQuery("?a=one+two&a=%2B&empty");
            writeLn(pairs);
            writeLn(url.formatQuery(pairs));
            try { url.parse(4); } catch (TypeError e) { writeLn(e.code + " " + e.message); }
            """,
            "[\"https\", \"example.test\", 8443, \"/a\", \"q=tea\", \"top\"]\nhttps://example.test/c?q=tea\n"
            + "[[\"a\", \"one two\"], [\"a\", \"+\"], [\"empty\", \"\"]]\na=one+two&a=%2B&empty=\n"
            + "WL3001 The argument of 'parse' must be an absolute URL string, not the integer 4.\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CsvRoundTripsArraysAndHeaderKeyedMaps(bool stack)
    {
        Run(stack, """
            import csv from "std:csv";
            var rows = [["tea, hot", "say \"hi\""], ["line\nline", ""]];
            var decoded = csv.parse(csv.stringify(rows));
            writeLn([decoded[0][0], decoded[0][1], decoded[1][0].length, decoded[1][1]]);
            var mapped = csv.parse(csv.stringify([{ price: "5", name: "tea" }], ["name", "price"]), true);
            writeLn([mapped[0]["name"], mapped[0]["price"]]);
            writeLn(csv.parse("a,b,\r\n"));
            try { csv.parse("a,a\n1,2", true); } catch (TypeError e) { writeLn(e.code + " " + e.message); }
            """,
            "[\"tea, hot\", \"say \\\"hi\\\"\", 9, \"\"]\n[\"tea\", \"5\"]\n[[\"a\", \"b\", \"\"]]\n"
            + "WL3001 The first argument of 'parse', the text, must be a CSV string, not text with duplicate column headers.\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GzipAndZipRoundTripBytesAndEnforceDecompressionLimits(bool stack)
    {
        Run(stack, """
            import compression from "std:compression";
            import { Bytes } from "std:bytes";
            var gzip = compression.gzip(Bytes.from("tea"));
            writeLn(compression.gunzip(gzip, 3).text());
            writeLn(compression.gunzip(Bytes.concat([gzip, gzip]), 6).text());
            writeLn(compression.gunzip(compression.gzip(Bytes.from("")), 0).length);
            var archive = compression.zip({
                "menu/tea.txt": Bytes.from("tea"),
                "tea.bin": Bytes.from([0, 255]),
                "empty": Bytes.from("")
            });
            var entries = compression.unzip(archive, 5);
            writeLn([entries["menu/tea.txt"].text(), entries["tea.bin"].toHex(), entries["empty"].length]);
            try { compression.gunzip(gzip, 2); } catch (TypeError e) { writeLn(e.code + " " + e.message); }
            try { compression.unzip(archive, 4); } catch (TypeError e) { writeLn(e.code + " " + e.message); }
            try { compression.gunzip(gzip.slice(0, gzip.length - 1), 3); } catch (TypeError e) { writeLn(e.code + " " + e.message); }
            try { compression.unzip(Bytes.from("not zip"), 100); } catch (TypeError e) { writeLn(e.code + " " + e.message); }
            """,
            "tea\nteatea\n0\n[\"tea\", \"00ff\", 0]\n"
            + "WL3001 Decompressed data exceeds the limit of 2 bytes.\n"
            + "WL3001 Decompressed data exceeds the limit of 4 bytes.\n"
            + "WL3001 The argument of 'gunzip' contains invalid gzip data.\n"
            + "WL3001 The argument of 'unzip' is not a valid ZIP archive.\n");
    }

    private void Run(bool stack, string source, string expected)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "utility.wlg");
        File.WriteAllText(path, source);
        using var output = new StringWriter();
        using var error = new StringWriter();
        if (stack)
        {
            Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["compile", "--stack", path], output, error));
            path = Path.ChangeExtension(path, ".wlbc");
            output.GetStringBuilder().Clear();
        }
        var exit = WebLingoCommandLine.Run(["run", path], output, error);
        Assert.Equal((WebLingoCommandLine.Success, expected, ""), (exit, output.ToString(), error.ToString()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
