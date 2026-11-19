using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Compression;

/// <summary>Gzip data and ZIP archives, without reading or writing files.</summary>
public sealed class WebLingoCompressionModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Function("gzip", 1, Gzip);
        registry.Function("gunzip", 2, Gunzip);
        registry.Function("zip", 1, Zip);
        registry.Function("unzip", 2, Unzip);
    }

    private static bool Gzip(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryBytes(args, "gzip", heap, out var data, out error)) return false;
        // GZipStream does not emit a member for an empty write.
        if (data.Length == 0)
            return heap.TryAllocateBytes(Convert.FromHexString("1F8B08000000000000FF03000000000000000000"), out result, out error);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(data);
        return heap.TryAllocateBytes(output.ToArray(), out result, out error);
    }

    private static bool Gunzip(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryBytes(args, "gunzip", heap, out var data, out error) || !TryLimit(args[1], "gunzip", heap, out var limit, out error))
            return false;
        if (data.Length < 20 || data[0] != 0x1F || data[1] != 0x8B || data[2] != 8)
        {
            error = Invalid("The argument of 'gunzip' is not gzip data.");
            return false;
        }

        try
        {
            using var input = new MemoryStream(data, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            if (!TryRead(gzip, new byte[8192], limit, limit, heap, out var decoded, out error)) return false;
            // GZipStream can end at EOF without rejecting a truncated trailer. Check the final member's trailer.
            var memberLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4));
            var checksum = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 8));
            if (memberLength > decoded.Length
                || checksum != WebLingoChecksums.Crc32(decoded, decoded.Length - (int)memberLength, (int)memberLength))
            {
                error = Invalid("The argument of 'gunzip' contains invalid gzip data.");
                return false;
            }
            return heap.TryAllocateBytes(decoded, out result, out error);
        }
        catch (InvalidDataException)
        {
            error = Invalid("The argument of 'gunzip' contains invalid gzip data.");
            return false;
        }
    }

    private static bool Zip(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var value = heap.Held(args[0]);
        var expected = new WebLingoArgument("zip", 0, 1, "the entries", "a map of file names to Bytes");
        if (value.Kind != WebLingoValueKind.Map)
        {
            error = expected.Wrong(args[0], heap);
            return false;
        }

        using var output = new MemoryStream();
        try
        {
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var count = heap.MapCount(value);
            for (var index = 0; index < count; index++)
            {
                var (key, contents) = heap.MapEntryAt(value, index);
                var nameValue = heap.Held(key);
                var name = nameValue.Kind == WebLingoValueKind.String ? heap.GetString(nameValue) : "";
                var bytes = heap.Held(contents);
                if (name.Length == 0 || name.EndsWith('/') || name.Contains('\0') || bytes.Kind != WebLingoValueKind.Bytes)
                {
                    error = expected.Wrong("a map containing an invalid file name or a value that is not Bytes");
                    return false;
                }
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(heap.GetBytes(bytes));
            }
        }
        catch (ArgumentException)
        {
            error = expected.Wrong("a map containing a file name that cannot be written to ZIP");
            return false;
        }
        return heap.TryAllocateBytes(output.ToArray(), out result, out error);
    }

    private static bool Unzip(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryBytes(args, "unzip", heap, out var data, out error) || !TryLimit(args[1], "unzip", heap, out var limit, out error))
            return false;
        if (!heap.TryAllocateMap([], 0, 0, out var map, out error)) return false;

        var roots = new WebLingoValue[] { map, WebLingoValue.Null, WebLingoValue.Null };
        heap.AddRoots(roots);
        try
        {
            using var input = new MemoryStream(data, writable: false);
            using var zip = new ZipArchive(input, ZipArchiveMode.Read);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var buffer = new byte[8192];
            var remaining = limit;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                if (entry.FullName.Length == 0 || !names.Add(entry.FullName))
                {
                    error = Invalid("The ZIP archive contains a missing or duplicate file name.");
                    return false;
                }
                if (!heap.TryAllocateString(entry.FullName, out roots[1], out error)) return false;
                using var stream = entry.Open();
                if (!TryRead(stream, buffer, remaining, limit, heap, out var decoded, out error)) return false;
                if (entry.Length != decoded.Length || entry.Crc32 != WebLingoChecksums.Crc32(decoded, 0, decoded.Length))
                {
                    error = Invalid("The ZIP archive contains a damaged file.");
                    return false;
                }
                if (!heap.TryAllocateBytes(decoded, out roots[2], out error)) return false;
                if (!heap.TrySetInMap(map, roots[1], roots[2], out error)) return false;
                remaining -= decoded.Length;
            }
            result = map;
            return true;
        }
        catch (InvalidDataException)
        {
            error = Invalid("The argument of 'unzip' is not a valid ZIP archive.");
            return false;
        }
        finally
        {
            heap.RemoveLastRoots(roots);
        }
    }

    private static bool TryBytes(ReadOnlySpan<WebLingoValue> args, string member, WebLingoHeap heap, out byte[] data,
        out WebLingoRuntimeError? error)
    {
        var value = heap.Held(args[0]);
        if (value.Kind == WebLingoValueKind.Bytes)
        {
            data = heap.GetBytes(value);
            error = null;
            return true;
        }
        data = [];
        error = new WebLingoArgument(member, 0, args.Length, "the data", "Bytes").Wrong(args[0], heap);
        return false;
    }

    private static bool TryLimit(WebLingoValue argument, string member, WebLingoHeap heap, out int limit,
        out WebLingoRuntimeError? error)
    {
        var value = heap.Held(argument);
        if (value.Kind == WebLingoValueKind.Int64 && value.AsInt64 is >= 0 and <= int.MaxValue)
        {
            limit = (int)value.AsInt64;
            error = null;
            return true;
        }
        limit = 0;
        error = new WebLingoArgument(member, 1, 2, "the decompressed byte limit", "an integer from 0 to 2147483647")
            .Wrong(argument, heap);
        return false;
    }

    private static bool TryRead(Stream input, byte[] buffer, int remaining, int limit, WebLingoHeap heap,
        out byte[] bytes, out WebLingoRuntimeError? error)
    {
        bytes = [];
        using var output = new MemoryStream();
        while (true)
        {
            var count = input.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, 1L + remaining - output.Length)));
            if (count == 0)
            {
                bytes = output.ToArray();
                error = null;
                return true;
            }
            var length = output.Length + count;
            if (length > remaining)
            {
                error = Invalid($"Decompressed data exceeds the limit of {limit.ToString(CultureInfo.InvariantCulture)} bytes.");
                return false;
            }
            if (!heap.TryFitBytes((int)length, out error)) return false;
            output.Write(buffer.AsSpan(0, count));
        }
    }

    private static WebLingoRuntimeError Invalid(string message) => new(WebLingoDiagnosticCodes.InvalidOperation, message);
}
