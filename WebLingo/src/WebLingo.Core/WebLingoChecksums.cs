namespace WebLingo.Core;

/// <summary>Checksums shared by the bytecode format and native modules.</summary>
public static class WebLingoChecksums
{
    private static readonly uint[] Crc32Table = MakeCrc32Table();

    /// <summary>The IEEE 802.3 CRC-32 used by gzip, ZIP and WebLingo bytecode.</summary>
    public static uint Crc32(byte[] bytes, int start, int count)
    {
        var crc = 0xFFFFFFFFu;
        for (var i = start; i < start + count; i++) crc = Crc32Table[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] MakeCrc32Table()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
