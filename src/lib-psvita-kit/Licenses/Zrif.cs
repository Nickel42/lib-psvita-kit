namespace LibPsVitaKit.Licenses;

/// <summary>
/// zRIF: a NoNpDrm license (<c>work.bin</c>, a 512-byte RIF) compressed with zlib against a fixed
/// preset dictionary, then base64-encoded. The same strings NoPayStation and pkg2zip use.
/// </summary>
public static class Zrif
{
    /// <summary>Size of a decoded PS Vita RIF (work.bin).</summary>
    public const int RifSize = 512;

    /// <summary>The RIF (work.bin bytes) a zRIF string holds.</summary>
    /// <exception cref="NotImplementedException">Not implemented yet; see docs/SPEC.md.</exception>
    public static byte[] Decode(string zrif) =>
        throw new NotImplementedException("zRIF decoding is not implemented yet.");

    /// <summary>The zRIF string for a RIF (work.bin bytes).</summary>
    /// <exception cref="NotImplementedException">Not implemented yet; see docs/SPEC.md.</exception>
    public static string Encode(ReadOnlySpan<byte> rif) =>
        throw new NotImplementedException("zRIF encoding is not implemented yet.");
}
