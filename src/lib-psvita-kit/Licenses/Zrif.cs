using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace LibPsVitaKit.Licenses;

/// <summary>
/// zRIF: a NoNpDrm license (a RIF, written as <c>work.bin</c>) compressed with zlib against a fixed
/// preset dictionary, then base64-encoded. The strings NoPayStation lists and pkg2zip takes.
/// </summary>
public static class Zrif
{
    /// <summary>Size of a PS Vita RIF (<c>sce_sys/package/work.bin</c>).</summary>
    public const int VitaRifSize = 512;
    /// <summary>Size of a PlayStation Mobile RIF (<c>RO/License/FAKE.rif</c>).</summary>
    public const int PsmRifSize = 1024;
    /// <summary>The zlib dictionary ID (its Adler-32) that zRIF streams name.</summary>
    public const uint DictionaryId = 0x627D1D5D;

    /// <summary>
    /// The preset dictionary: mostly zeros, then the content-ID fragments RIFs share. Stored the way
    /// pkg2zip's rif2zrif.py stores it (public domain, github.com/mmozeiko/pkg2zip).
    /// </summary>
    internal static readonly byte[] Dictionary = LoadDictionary();

    private static byte[] LoadDictionary()
    {
        const string packed = "eNpjYBgFo2AU0AsYAIElGt8MRJiDCAsw3xhEmIAIU4N4AwNdRxcXZ3+/EJCAkW6Ac7C7ARwYgviuQAaIdoPSzlDaBUo7QmknIM3ACIZM78+u7kx3VWYEAGJ9HV0=";
        using var z = new ZLibStream(new MemoryStream(Convert.FromBase64String(packed)), CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>The RIF a zRIF string holds: 512 bytes for a Vita title, 1024 for PSM.</summary>
    /// <exception cref="VitaFormatException">Not a valid zRIF: bad base64, bad header, unknown dictionary, corrupt data, wrong checksum or size.</exception>
    public static byte[] Decode(string zrif)
    {
        ArgumentNullException.ThrowIfNull(zrif);
        var raw = FromBase64(zrif);
        if (raw.Length < 2 + 4) throw new VitaFormatException("zRIF is too short.");
        if (((raw[0] << 8) | raw[1]) % 31 != 0) throw new VitaFormatException("zRIF header is corrupted.");
        if ((raw[0] & 0x0F) != 8) throw new VitaFormatException("zRIF is not deflate-compressed.");

        // .NET's inflater takes no preset dictionary, so put the dictionary in front of the stream as
        // a stored (uncompressed) block: the stream's own blocks start on a byte boundary after it and
        // can refer back into it. The first Dictionary.Length bytes out are then the dictionary itself.
        var usesDictionary = (raw[1] & 0x20) != 0;
        var bodyStart = 2;
        using var input = new MemoryStream();
        if (usesDictionary)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(2)) != DictionaryId) throw new VitaFormatException("zRIF uses an unknown dictionary.");
            bodyStart = 6;
            Span<byte> stored = stackalloc byte[5];
            stored[0] = 0; // BFINAL = 0, BTYPE = 00 (stored), rest of the byte is padding
            BinaryPrimitives.WriteUInt16LittleEndian(stored[1..], (ushort)Dictionary.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(stored[3..], (ushort)~Dictionary.Length);
            input.Write(stored);
            input.Write(Dictionary);
        }
        input.Write(raw, bodyStart, raw.Length - bodyStart);
        input.Position = 0;

        var prefix = usesDictionary ? Dictionary.Length : 0;
        var buffer = new byte[prefix + PsmRifSize + 1];
        int total = 0;
        try
        {
            using var inflate = new DeflateStream(input, CompressionMode.Decompress);
            int n;
            while (total < buffer.Length && (n = inflate.Read(buffer, total, buffer.Length - total)) > 0) total += n;
        }
        catch (InvalidDataException)
        {
            throw new VitaFormatException("zRIF data is corrupted.");
        }

        var rif = buffer.AsSpan(prefix, Math.Max(0, total - prefix));
        if (rif.Length is not (VitaRifSize or PsmRifSize))
            throw new VitaFormatException($"zRIF decodes to {rif.Length} bytes; a RIF is {VitaRifSize} (Vita) or {PsmRifSize} (PSM).");
        if (!HasChecksum(raw, bodyStart, Adler32(rif))) throw new VitaFormatException("zRIF is corrupted: wrong checksum.");
        return rif.ToArray();
    }

    /// <summary>Like <see cref="Decode"/>, but false instead of an exception.</summary>
    public static bool TryDecode(string? zrif, out byte[] rif)
    {
        rif = [];
        if (zrif is null) return false;
        try
        {
            rif = Decode(zrif);
            return true;
        }
        catch (VitaFormatException)
        {
            return false;
        }
    }

    /// <summary>The content ID a RIF is for: at 0x10 in a Vita RIF, 0x50 in a PSM one.</summary>
    /// <exception cref="VitaFormatException">Not 512 or 1024 bytes.</exception>
    public static string ContentIdOf(ReadOnlySpan<byte> rif)
    {
        var offset = rif.Length switch
        {
            VitaRifSize => 0x10,
            PsmRifSize => 0x50,
            _ => throw new VitaFormatException($"A RIF is {VitaRifSize} or {PsmRifSize} bytes, not {rif.Length}."),
        };
        var field = rif.Slice(offset, 0x30);
        var end = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? field : field[..end]);
    }

    /// <summary>The zRIF string for a RIF (work.bin bytes).</summary>
    /// <exception cref="NotImplementedException">Not implemented yet: .NET's zlib takes no preset dictionary; see docs/SPEC.md.</exception>
    public static string Encode(ReadOnlySpan<byte> rif) =>
        throw new NotImplementedException("zRIF encoding is not implemented yet.");

    /// <summary>Base64, with or without '=' padding, as pkg2zip accepts.</summary>
    private static byte[] FromBase64(string s)
    {
        s = s.Trim();
        if (s.Length % 4 != 0 && !s.EndsWith('=')) s += new string('=', 4 - s.Length % 4);
        try
        {
            return Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            throw new VitaFormatException("zRIF is not valid base64.");
        }
    }

    /// <summary>
    /// The Adler-32 trailer ends the zlib stream, but zRIF tools pad the bytes to whole base64 groups
    /// (rif2zrif.py adds up to two zero bytes), so it sits 0–2 bytes before the end.
    /// </summary>
    private static bool HasChecksum(byte[] raw, int bodyStart, uint adler)
    {
        for (var pad = 0; pad <= 2; pad++)
        {
            var at = raw.Length - 4 - pad;
            if (at <= bodyStart) break;
            if (BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(at)) == adler && raw.AsSpan(at + 4).IndexOfAnyExcept((byte)0) < 0) return true;
        }
        return false;
    }

    internal static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        foreach (var x in data)
        {
            a = (a + x) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }
}
