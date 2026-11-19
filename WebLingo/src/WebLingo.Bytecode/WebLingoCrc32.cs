using WebLingo.Core;
namespace WebLingo.Bytecode;

/// <summary>
/// The CRC-32 of IEEE 802.3, as ZIP and PNG compute it (reflected polynomial 0xEDB88320, starting from and finished
/// with 0xFFFFFFFF), which a WLBC file's directory gives each section (specification, section 5.3, the owner's answers).
/// </summary>
public static class WebLingoCrc32
{
    /// <summary>The CRC-32 of <paramref name="count"/> bytes of <paramref name="bytes"/> from <paramref name="start"/>.</summary>
    public static uint Compute(byte[] bytes, int start, int count) => WebLingoChecksums.Crc32(bytes, start, count);
}
