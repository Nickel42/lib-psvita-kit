using System.Buffers.Binary;
using System.Text;

namespace LibPsVitaKit.Pkg;

/// <summary>
/// The fixed header at the start of a PlayStation Store <c>.pkg</c> (big-endian). Only the fields
/// needed to identify the package and find its encrypted body; the metadata and item table come later.
/// </summary>
public sealed record PkgHeader(
    ushort Revision,
    ushort Type,
    uint MetaOffset,
    uint MetaCount,
    uint HeaderSize,
    uint ItemCount,
    ulong TotalSize,
    ulong DataOffset,
    ulong DataSize,
    string ContentId,
    byte[] Iv,
    byte KeyType)
{
    /// <summary>Bytes read from the start of the file: the main header plus the extended header with the key type.</summary>
    public const int ReadSize = 0x100;

    public static bool HasMagic(ReadOnlySpan<byte> start) =>
        start.Length >= 4 && start[0] == 0x7F && start[1] == (byte)'P' && start[2] == (byte)'K' && start[3] == (byte)'G';

    public static PkgHeader Read(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    /// <exception cref="VitaFormatException">Not a .pkg file, or too short.</exception>
    public static PkgHeader Read(Stream stream)
    {
        var buffer = new byte[ReadSize];
        var read = stream.ReadAtLeast(buffer, ReadSize, throwOnEndOfStream: false);
        return Parse(buffer.AsSpan(0, read));
    }

    /// <exception cref="VitaFormatException">Not a .pkg file, or too short.</exception>
    public static PkgHeader Parse(ReadOnlySpan<byte> h)
    {
        if (!HasMagic(h)) throw new VitaFormatException("Not a .pkg file.");
        if (h.Length < ReadSize) throw new VitaFormatException(".pkg header is truncated.");
        return new PkgHeader(
            Revision: BinaryPrimitives.ReadUInt16BigEndian(h[0x04..]),
            Type: BinaryPrimitives.ReadUInt16BigEndian(h[0x06..]),
            MetaOffset: BinaryPrimitives.ReadUInt32BigEndian(h[0x08..]),
            MetaCount: BinaryPrimitives.ReadUInt32BigEndian(h[0x0C..]),
            HeaderSize: BinaryPrimitives.ReadUInt32BigEndian(h[0x10..]),
            ItemCount: BinaryPrimitives.ReadUInt32BigEndian(h[0x14..]),
            TotalSize: BinaryPrimitives.ReadUInt64BigEndian(h[0x18..]),
            DataOffset: BinaryPrimitives.ReadUInt64BigEndian(h[0x20..]),
            DataSize: BinaryPrimitives.ReadUInt64BigEndian(h[0x28..]),
            ContentId: Encoding.ASCII.GetString(h.Slice(0x30, Ids.ContentId.Length)).TrimEnd('\0'),
            Iv: h.Slice(0x70, 16).ToArray(),
            KeyType: (byte)(h[0xE7] & 7));
    }
}
