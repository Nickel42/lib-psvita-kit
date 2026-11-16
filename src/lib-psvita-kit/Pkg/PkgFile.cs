using System.Buffers.Binary;
using System.Text;
using LibPsVitaKit.Crypto;
using LibPsVitaKit.Ids;
using LibPsVitaKit.Sfo;

namespace LibPsVitaKit.Pkg;

/// <summary>What a package holds, from its content type (and, for a Vita game, its SFO category).</summary>
public enum PkgContentKind
{
    Unknown,
    /// <summary>A Vita game or app (content type 0x15).</summary>
    VitaApp,
    /// <summary>Vita DLC (content type 0x16).</summary>
    VitaDlc,
    /// <summary>A Vita update: content type 0x15 with SFO category "gp".</summary>
    VitaPatch,
    /// <summary>A PlayStation Mobile title (content type 0x18 or 0x1D).</summary>
    Psm,
    /// <summary>A PSP game (content types 0x07, 0x0E, 0x0F, 0x10). Recognised only.</summary>
    Psp,
    /// <summary>A PS1 classic (content type 0x06). Recognised only.</summary>
    Psx,
}

/// <summary>One entry in a package's item table: a file or a folder, named relative to the game folder.</summary>
public sealed record PkgItem(string Name, ulong NameOffset, ulong DataOffset, ulong DataSize, byte Flags)
{
    /// <summary>Folders have flags 4 or 18.</summary>
    public bool IsFolder => Flags is 4 or 18;
}

/// <summary>
/// A PlayStation Store package: header, metadata and the unencrypted param.sfo copy. Reading
/// these needs no keys; the item table and files (<see cref="ReadItems"/>) do.
/// </summary>
public sealed class PkgFile
{
    /// <summary>"\x7Fext": the extended header that follows the 0xC0-byte main header in Vita packages.</summary>
    private const uint ExtendedMagic = 0x7F657874;
    private const int MaxMetadataEntry = 0x1000, MaxSfoSize = 0x10000, ItemSize = 32;
    /// <summary>Largest name area read in one piece; beyond this, names are read one by one.</summary>
    private const ulong MaxNameSpan = 16 << 20;

    public required string Path { get; init; }
    public required long Length { get; init; }
    public required PkgHeader Header { get; init; }
    public required uint ContentType { get; init; }
    public required PkgContentKind Kind { get; init; }
    /// <summary>The item table's offset and size, relative to <see cref="PkgHeader.DataOffset"/>.</summary>
    public required uint ItemsOffset { get; init; }
    public required uint ItemsSize { get; init; }
    /// <summary>The package's param.sfo, from its metadata; null for PSM, PSP and PS1 packages.</summary>
    public SfoFile? Sfo { get; init; }

    /// <summary>The content ID: from the SFO for Vita games, DLC and updates, otherwise from the header.</summary>
    public string ContentId => Sfo?.ContentId ?? Header.ContentId;
    public string? TitleId => Ids.ContentId.TryParse(ContentId, out var id) ? id.TitleId.Value : null;
    public string? Title => Sfo?.GetText("STITLE") ?? Sfo?.Title;
    public bool IsVita => Kind is PkgContentKind.VitaApp or PkgContentKind.VitaDlc or PkgContentKind.VitaPatch or PkgContentKind.Psm;

    /// <exception cref="VitaFormatException">Not a package, or damaged.</exception>
    public static PkgFile Open(string path)
    {
        using var fs = File.OpenRead(path);
        var header = PkgHeader.Read(fs);
        Span<byte> ext = stackalloc byte[4];
        ReadAt(fs, 0xC0, ext);
        if (BinaryPrimitives.ReadUInt32BigEndian(ext) != ExtendedMagic) throw new VitaFormatException("Not a PS Vita .pkg (no extended header).");
        if ((ulong)fs.Length < header.TotalSize) throw new VitaFormatException($".pkg is truncated: {fs.Length} of {header.TotalSize} bytes.");

        uint contentType = 0, itemsOffset = 0, itemsSize = 0, sfoOffset = 0, sfoSize = 0;
        long at = header.MetaOffset;
        Span<byte> entry = stackalloc byte[8];
        var data = new byte[MaxMetadataEntry];
        for (var i = 0; i < header.MetaCount; i++)
        {
            ReadAt(fs, at, entry);
            var type = BinaryPrimitives.ReadUInt32BigEndian(entry);
            var size = BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            if (size > MaxMetadataEntry) throw new VitaFormatException($".pkg metadata entry {i} is {size} bytes; it is probably damaged.");
            var value = data.AsSpan(0, (int)size);
            ReadAt(fs, at + 8, value);
            switch (type)
            {
                case 2 when size >= 4: contentType = BinaryPrimitives.ReadUInt32BigEndian(value); break;
                case 13 when size >= 8: (itemsOffset, itemsSize) = (BinaryPrimitives.ReadUInt32BigEndian(value), BinaryPrimitives.ReadUInt32BigEndian(value[4..])); break;
                case 14 when size >= 8: (sfoOffset, sfoSize) = (BinaryPrimitives.ReadUInt32BigEndian(value), BinaryPrimitives.ReadUInt32BigEndian(value[4..])); break;
            }
            at += 8 + size;
        }
        if (header.DataOffset + itemsOffset + (ulong)header.ItemCount * ItemSize > (ulong)fs.Length)
            throw new VitaFormatException(".pkg item table lies outside the file.");

        var kind = contentType switch
        {
            0x06 => PkgContentKind.Psx,
            0x07 or 0x0E or 0x0F or 0x10 => PkgContentKind.Psp,
            0x15 => PkgContentKind.VitaApp,
            0x16 => PkgContentKind.VitaDlc,
            0x18 or 0x1D => PkgContentKind.Psm,
            _ => PkgContentKind.Unknown,
        };
        SfoFile? sfo = null;
        if (kind is PkgContentKind.VitaApp or PkgContentKind.VitaDlc)
        {
            if (sfoSize is < 20 or > MaxSfoSize || sfoOffset + (long)sfoSize > fs.Length) throw new VitaFormatException(".pkg has no readable param.sfo.");
            var bytes = new byte[sfoSize];
            ReadAt(fs, sfoOffset, bytes);
            sfo = SfoFile.Parse(bytes);
            if (kind == PkgContentKind.VitaApp && sfo.Category == "gp") kind = PkgContentKind.VitaPatch;
        }
        return new PkgFile
        {
            Path = path, Length = fs.Length, Header = header, ContentType = contentType, Kind = kind,
            ItemsOffset = itemsOffset, ItemsSize = itemsSize, Sfo = sfo,
        };
    }

    /// <summary>Decrypts the item table and the items' names with the package's content key.</summary>
    /// <exception cref="VitaFormatException">An item points outside the file or has a name that isn't a safe relative path.</exception>
    public IReadOnlyList<PkgItem> ReadItems(ReadOnlySpan<byte> contentKey) => ReadItems(contentKey, MaxNameSpan);

    internal IReadOnlyList<PkgItem> ReadItems(ReadOnlySpan<byte> contentKey, ulong maxNameSpan)
    {
        using var aes = AesCtr.Create(contentKey);
        using var fs = File.OpenRead(Path);
        var enc = (long)Header.DataOffset;
        var table = new byte[Header.ItemCount * ItemSize];
        ReadAt(fs, enc + ItemsOffset, table);
        AesCtr.Xor(aes, Header.Iv, ItemsOffset / 16, table);

        var entries = new (uint NameOffset, uint NameSize, ulong DataOffset, ulong DataSize, byte Flags)[Header.ItemCount];
        ulong first = ulong.MaxValue, end = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            var e = table.AsSpan(i * ItemSize, ItemSize);
            var nameOffset = BinaryPrimitives.ReadUInt32BigEndian(e);
            var nameSize = BinaryPrimitives.ReadUInt32BigEndian(e[4..]);
            var dataOffset = BinaryPrimitives.ReadUInt64BigEndian(e[8..]);
            var dataSize = BinaryPrimitives.ReadUInt64BigEndian(e[16..]);
            if (nameOffset % 16 != 0 || dataOffset % 16 != 0) throw new VitaFormatException($".pkg item {i} isn't 16-byte aligned; the key may be wrong.");
            if (nameSize is 0 or > 1024 || (ulong)enc + nameOffset + nameSize > (ulong)Length || (ulong)enc + dataOffset + dataSize > (ulong)Length)
                throw new VitaFormatException($".pkg item {i} lies outside the file; the package is damaged or the key is wrong.");
            entries[i] = (nameOffset, nameSize, dataOffset, dataSize, e[27]);
            first = Math.Min(first, nameOffset);
            end = Math.Max(end, (ulong)nameOffset + nameSize);
        }

        // Names usually sit together after the table: read and decrypt them in one go, unless they're spread out.
        byte[]? names = null;
        if (entries.Length > 0 && end - first <= maxNameSpan)
        {
            names = new byte[end - first];
            ReadAt(fs, enc + (long)first, names);
            AesCtr.Xor(aes, Header.Iv, first / 16, names);
        }
        var items = new List<PkgItem>(entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            var (nameOffset, nameSize, dataOffset, dataSize, flags) = entries[i];
            byte[] name;
            var start = 0;
            if (names is not null)
            {
                (name, start) = (names, (int)(nameOffset - first));
            }
            else
            {
                name = new byte[nameSize];
                ReadAt(fs, enc + nameOffset, name);
                AesCtr.Xor(aes, Header.Iv, nameOffset / 16, name);
            }
            var text = Encoding.UTF8.GetString(name, start, (int)nameSize).TrimEnd(' ');
            items.Add(new PkgItem(CheckName(text, i), nameOffset, dataOffset, dataSize, flags));
        }
        return items;
    }

    /// <summary>Names are used as paths, so refuse anything that could leave the output folder.</summary>
    private static string CheckName(string name, int index)
    {
        var parts = name.Split('/');
        if (name.Length == 0 || name.Contains('\\') || name.Contains(':') || name[0] == '/' || parts.Any(p => p is "" or "." or ".."))
            throw new VitaFormatException($".pkg item {index} has an unsafe name '{name}'.");
        return name;
    }

    internal static void ReadAt(FileStream fs, long offset, Span<byte> buffer)
    {
        fs.Position = offset;
        if (fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) < buffer.Length)
            throw new VitaFormatException(".pkg is truncated.");
    }
}
