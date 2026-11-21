using System.IO.Compression;
using WebLingo.Cli;
using WebLingo.Core;
using WebLingo.Runtime;
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
            import { writeLn } from "std:console";
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
            import { writeLn } from "std:console";
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
            import { writeLn } from "std:console";
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
    public void CsvPreservesALiteralLeadingBomInDataAndHeaders(bool stack)
    {
        Run(stack, """
            import { writeLn } from "std:console";
            import csv from "std:csv";
            var literal = "\u{FEFF}Tea";
            var text = csv.stringify([[literal, "green"], [literal, "black"]]);
            var rows = csv.parse(text);
            writeLn([text.startsWith("\""), rows[0][0] == literal, rows[1][0] == literal]);
            var maps = [{ color: "green" }];
            maps[0][literal] = "Sencha";
            var headed = csv.stringify(maps, [literal, "color"]);
            var decoded = csv.parse(headed, true)[0];
            writeLn([headed.startsWith("\""), decoded?[literal] == "Sencha", decoded["color"]]);
            """, "[true, true, true]\n[true, true, \"green\"]\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CsvConsumesOnlyTheBomAtTheStartOfTheText(bool stack)
    {
        Run(stack, """
            import { writeLn } from "std:console";
            import csv from "std:csv";
            var raw = "\u{FEFF}Tea,\u{FEFF}green\r\n";
            var quoted = "\u{FEFF}\"\u{FEFF}Tea\",green\r\n";
            writeLn([csv.parse(raw)[0][0] == "Tea", csv.parse(raw)[0][1] == "\u{FEFF}green",
                csv.parse(quoted)[0][0] == "\u{FEFF}Tea"]);
            """, "[true, true, true]\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GzipAndZipRoundTripBytesAndEnforceDecompressionLimits(bool stack)
    {
        Run(stack, """
            import { writeLn } from "std:console";
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

    [Theory]
    [InlineData(false, 1024, false, 2048, null)]
    [InlineData(true, 1024, false, 2048, null)]
    [InlineData(false, 1024, true, 2048, null)]
    [InlineData(true, 1024, true, 2048, null)]
    [InlineData(false, 1024, false, 2047, WebLingoDiagnosticCodes.InvalidOperation)]
    [InlineData(true, 1024, false, 2047, WebLingoDiagnosticCodes.InvalidOperation)]
    [InlineData(false, 250, false, 2048, WebLingoDiagnosticCodes.BudgetExceeded)]
    [InlineData(true, 250, false, 2048, WebLingoDiagnosticCodes.BudgetExceeded)]
    public void DecompressionUsesByteUnitsAndCollectsBeforeRefusing(bool zip, int heapUnits, bool garbage, int limit, string? code)
    {
        var original = new byte[2048];
        using var compressed = new MemoryStream();
        if (zip)
        {
            using var archive = new ZipArchive(compressed, ZipArchiveMode.Create, leaveOpen: true);
            using var entry = archive.CreateEntry("entry").Open();
            entry.Write(original);
        }
        else
        {
            using var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true);
            gzip.Write(original);
        }

        var heap = new WebLingoHeap(heapUnits);
        var args = new[] { WebLingoValue.Null, WebLingoValue.FromInt64(limit) };
        heap.AddRoots(args);
        Assert.True(heap.TryAllocateBytes(compressed.ToArray(), out args[0], out _));
        if (garbage) Assert.True(heap.TryAllocateString(new string('x', 800), out _, out _));
        Assert.Equal(0, heap.CollectionCount);

        var loader = new WebLingoNativeModuleLoader(
            new Dictionary<string, string> { ["std"] = Path.Combine(AppContext.BaseDirectory, "std") }, new WebLingoBuiltinRegistry());
        Assert.True(loader.TryResolve("program.wlg", "std:compression", out var module, out var why), why);
        var function = loader.FunctionsOf(module!).Single(item => item.Name == (zip ? "unzip" : "gunzip"));
        var success = function.TryInvoke(args, heap, out var decoded, out var error);
        Assert.Equal(code, error?.Code);
        Assert.Equal(code is null, success);
        if (success)
        {
            if (zip) decoded = heap.MapEntryAt(decoded, 0).Value;
            Assert.Equal(original, heap.GetBytes(decoded));
        }
        if (garbage) Assert.True(heap.CollectionCount > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeededRandomNumbersHaveAStableSequenceAndValidBounds(bool stack)
    {
        Run(stack, """
            import { writeLn } from "std:console";
            import { Random } from "std:random";
            var first = new Random(42);
            var second = new Random(42);
            writeLn([first.nextInt(0, 100), first.nextInt(0, 100), first.nextInt(0, 100)]);
            writeLn([second.nextInt(0, 100), second.nextInt(0, 100), second.nextInt(0, 100)]);
            var fraction = new Random(42).next();
            writeLn([fraction == 0.7415648787718233, fraction >= 0.0, fraction < 1.0]);
            var low = -9223372036854775807 - 1;
            var high = 9223372036854775807;
            var wide = new Random(-1).nextInt(low, high);
            writeLn([wide >= low, wide < high, first.nextInt(-5, -4)]);
            try { first.nextInt(1, 1); } catch (TypeError e) { writeLn(e.code + " " + e.message); }
            try { new Random(1.5); } catch (TypeError e) { writeLn(e.code + " " + e.message); }
            """,
            "[13, 91, 58]\n[13, 91, 58]\n[true, true, true]\n[true, true, -5]\n"
            + "WL3001 The inclusive lower bound of 'nextInt' must be less than its exclusive upper bound.\n"
            + "WL3001 The argument of 'Random' must be an integer, not the number 1.5.\n");
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
            path = Path.ChangeExtension(path, ".wlbs");
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
