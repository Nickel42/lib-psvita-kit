using System.Buffers.Binary;
using System.Text;

namespace LibPsVitaKit.Sfo;

/// <summary>How an SFO entry's value is stored.</summary>
public enum SfoFormat : ushort
{
    /// <summary>UTF-8 text, not null-terminated (rare; used by a few system keys).</summary>
    Utf8Special = 0x0004,
    /// <summary>UTF-8 text, null-terminated.</summary>
    Utf8 = 0x0204,
    /// <summary>A 32-bit little-endian integer.</summary>
    Int32 = 0x0404,
}

/// <summary>One SFO value: text or a 32-bit integer, plus the space reserved for it.</summary>
public sealed record SfoEntry(SfoFormat Format, string? Text, int Integer, int MaxLength)
{
    public static SfoEntry FromText(string text, int maxLength = 0) => new(SfoFormat.Utf8, text, 0, maxLength);
    public static SfoEntry FromInt(int value) => new(SfoFormat.Int32, null, value, 4);
    public override string ToString() => Format == SfoFormat.Int32 ? $"0x{Integer:X8}" : Text ?? "";
}

/// <summary>
/// A <c>param.sfo</c> (PSF) file: the key/value table that names a PS Vita (or PSP, PS3) title.
/// Layout: "\0PSF", version, key table start, data table start, entry count, then 16-byte index
/// entries (key offset, format, length, max length, data offset). Keys are kept in file order on
/// read and written sorted, as the console's tools do.
/// </summary>
public sealed class SfoFile
{
    private static ReadOnlySpan<byte> Magic => "\0PSF"u8;
    private const int HeaderSize = 20, IndexEntrySize = 16;

    /// <summary>The PSF version; 1.1 (0x0101) on the Vita.</summary>
    public uint Version { get; set; } = 0x0101;

    /// <summary>The entries by key (case-sensitive).</summary>
    public Dictionary<string, SfoEntry> Entries { get; } = new(StringComparer.Ordinal);

    public string? Title => GetText("TITLE");
    public string? TitleId => GetText("TITLE_ID");
    public string? ContentId => GetText("CONTENT_ID");
    public string? Category => GetText("CATEGORY");
    public string? AppVersion => GetText("APP_VER");

    public string? GetText(string key) => Entries.TryGetValue(key, out var e) && e.Format != SfoFormat.Int32 ? e.Text : null;
    public int? GetInt(string key) => Entries.TryGetValue(key, out var e) && e.Format == SfoFormat.Int32 ? e.Integer : null;
    public void SetText(string key, string value, int maxLength = 0) => Entries[key] = SfoEntry.FromText(value, maxLength);
    public void SetInt(string key, int value) => Entries[key] = SfoEntry.FromInt(value);

    public static SfoFile Load(string path) => Parse(File.ReadAllBytes(path));

    public static SfoFile Read(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    /// <exception cref="VitaFormatException">The bytes are not a valid SFO.</exception>
    public static SfoFile Parse(ReadOnlySpan<byte> sfo)
    {
        if (sfo.Length < HeaderSize || !sfo[..4].SequenceEqual(Magic)) throw new VitaFormatException("Not a param.sfo file.");
        var file = new SfoFile { Version = BinaryPrimitives.ReadUInt32LittleEndian(sfo[4..]) };
        var keys = (int)BinaryPrimitives.ReadUInt32LittleEndian(sfo[8..]);
        var data = (int)BinaryPrimitives.ReadUInt32LittleEndian(sfo[12..]);
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(sfo[16..]);
        if (count < 0 || HeaderSize + (long)count * IndexEntrySize > sfo.Length || keys > sfo.Length || data > sfo.Length)
            throw new VitaFormatException("param.sfo is truncated.");
        for (var i = 0; i < count; i++)
        {
            var entry = sfo.Slice(HeaderSize + i * IndexEntrySize, IndexEntrySize);
            var keyStart = keys + BinaryPrimitives.ReadUInt16LittleEndian(entry);
            var format = (SfoFormat)BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var max = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = data + (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (keyStart >= sfo.Length || length < 0 || offset < 0 || offset + (long)length > sfo.Length)
                throw new VitaFormatException($"param.sfo entry {i} points outside the file.");
            var keyLength = sfo[keyStart..].IndexOf((byte)0);
            if (keyLength < 0) throw new VitaFormatException($"param.sfo entry {i} has an unterminated key.");
            var key = Encoding.UTF8.GetString(sfo.Slice(keyStart, keyLength));
            var value = sfo.Slice(offset, length);
            file.Entries[key] = format switch
            {
                SfoFormat.Int32 when length >= 4 => new SfoEntry(format, null, BinaryPrimitives.ReadInt32LittleEndian(value), max),
                SfoFormat.Utf8 or SfoFormat.Utf8Special => new SfoEntry(format, Encoding.UTF8.GetString(value).TrimEnd('\0'), 0, max),
                _ => throw new VitaFormatException($"param.sfo key {key} has unsupported format 0x{(ushort)format:X4}."),
            };
        }
        return file;
    }

    /// <summary>The file's bytes: keys sorted, key table padded to 4 bytes, each value padded to its max length.</summary>
    public byte[] ToArray()
    {
        var sorted = Entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        var keyTable = new MemoryStream();
        var keyOffsets = new List<int>();
        foreach (var (key, _) in sorted)
        {
            keyOffsets.Add((int)keyTable.Length);
            keyTable.Write(Encoding.UTF8.GetBytes(key));
            keyTable.WriteByte(0);
        }
        while (keyTable.Length % 4 != 0) keyTable.WriteByte(0);

        var values = sorted.Select(e => Encode(e.Value)).ToList();
        var keysStart = HeaderSize + sorted.Count * IndexEntrySize;
        var dataStart = keysStart + (int)keyTable.Length;
        var output = new byte[dataStart + values.Sum(v => v.Max)];
        var span = output.AsSpan();
        Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)keysStart);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)dataStart);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)sorted.Count);
        keyTable.ToArray().CopyTo(span[keysStart..]);
        var dataOffset = 0;
        for (var i = 0; i < sorted.Count; i++)
        {
            var (bytes, length, max) = values[i];
            var index = span.Slice(HeaderSize + i * IndexEntrySize, IndexEntrySize);
            BinaryPrimitives.WriteUInt16LittleEndian(index, (ushort)keyOffsets[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(index[2..], (ushort)sorted[i].Value.Format);
            BinaryPrimitives.WriteUInt32LittleEndian(index[4..], (uint)length);
            BinaryPrimitives.WriteUInt32LittleEndian(index[8..], (uint)max);
            BinaryPrimitives.WriteUInt32LittleEndian(index[12..], (uint)dataOffset);
            bytes.CopyTo(span[(dataStart + dataOffset)..]);
            dataOffset += max;
        }
        return output;
    }

    public void Write(Stream stream) => stream.Write(ToArray());
    public void Save(string path) => File.WriteAllBytes(path, ToArray());

    private static (byte[] Bytes, int Length, int Max) Encode(SfoEntry e)
    {
        if (e.Format == SfoFormat.Int32)
        {
            var b = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(b, e.Integer);
            return (b, 4, 4);
        }
        var text = Encoding.UTF8.GetBytes(e.Text ?? "");
        var length = e.Format == SfoFormat.Utf8 ? text.Length + 1 : text.Length;
        var max = Math.Max(e.MaxLength, length);
        max = (max + 3) & ~3;
        var bytes = new byte[length];
        text.CopyTo(bytes, 0);
        return (bytes, length, max);
    }
}
