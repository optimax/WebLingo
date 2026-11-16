namespace WebLingo.Bytecode;

/// <summary>
/// The CRC-32 of IEEE 802.3, as ZIP and PNG compute it (reflected polynomial 0xEDB88320, starting from and finished
/// with 0xFFFFFFFF), which a WLBC file's directory gives each section (specification, section 5.3, the owner's answers).
/// </summary>
public static class WebLingoCrc32
{
    private static readonly uint[] Table = MakeTable();

    /// <summary>The CRC-32 of <paramref name="count"/> bytes of <paramref name="bytes"/> from <paramref name="start"/>.</summary>
    public static uint Compute(byte[] bytes, int start, int count)
    {
        var crc = 0xFFFFFFFFu;
        for (var i = start; i < start + count; i++) crc = Table[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] MakeTable()
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
