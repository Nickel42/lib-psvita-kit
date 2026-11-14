using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using LibPsVitaKit.Keys;
using LibPsVitaKit.Sfo;

namespace LibPsVitaKit.Tests;

/// <summary>
/// Builds synthetic PS Vita packages, encrypted with made-up keys, laid out as pkg2zip reads them.
/// Its AES-CTR is written independently of the library's (BigInteger counter, one block at a time)
/// so that the two check each other.
/// </summary>
internal sealed class PkgBuilder
{
    public static readonly byte[][] TestKeys =
    [
        Convert.FromHexString("00112233445566778899AABBCCDDEEF0"),
        Convert.FromHexString("0F1E2D3C4B5A69788796A5B4C3D2E1F0"),
        Convert.FromHexString("F0E1D2C3B4A5968778695A4B3C2D1E0F"),
    ];

    public static PkgKeys Keys() => new PkgKeys().Set(2, TestKeys[0]).Set(3, TestKeys[1]).Set(4, TestKeys[2]);

    public uint ContentType { get; set; } = 0x15;
    public int KeyType { get; set; } = 2;
    public string ContentId { get; set; } = "UP0000-PCSE00000_00-0000000000000000";
    /// <summary>Written to the metadata unencrypted; null leaves it out (as PSM packages do).</summary>
    public SfoFile? Sfo { get; set; }
    /// <summary>Folders have null data. sce_sys/package/digs.bin is stored unencrypted, as in real packages.</summary>
    public List<(string Name, byte[]? Data)> Items { get; } = [];
    public byte[] Iv { get; set; } = Convert.FromHexString("A0A1A2A3A4A5A6A7A8A9AAABACADFFFE"); // near a carry
    public byte[] Tail { get; set; } = Encoding.ASCII.GetBytes("tail: signatures and digests live here....");

    /// <summary>Where the item table ends in the encrypted area: head.bin is the file up to here.</summary>
    public long HeadSize { get; private set; }

    public static SfoFile GameSfo(string contentId, string category = "gd")
    {
        var sfo = new SfoFile();
        sfo.SetText("TITLE", "Test Game", 0x80);
        sfo.SetText("TITLE_ID", contentId[7..16], 0x0C);
        sfo.SetText("CONTENT_ID", contentId, 0x30);
        sfo.SetText("CATEGORY", category, 4);
        sfo.SetText("APP_VER", "01.00", 8);
        return sfo;
    }

    public static byte[] Pattern(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    public byte[] Build()
    {
        static int Align(int n) => (n + 15) & ~15;

        // Encrypted area: item table, then names, then data, each 16-byte aligned.
        var table = new byte[Items.Count * 32];
        var names = new List<(int Offset, byte[] Bytes)>();
        var at = Align(table.Length);
        foreach (var (name, _) in Items)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            names.Add((at, bytes));
            at = Align(at + bytes.Length);
        }
        var itemsSize = at;
        var data = new List<(int Offset, byte[] Bytes)>();
        foreach (var (_, bytes) in Items)
        {
            data.Add((at, bytes ?? []));
            at = Align(at + (bytes?.Length ?? 0));
        }
        var enc = new byte[at];
        for (var i = 0; i < Items.Count; i++)
        {
            var e = table.AsSpan(i * 32, 32);
            BinaryPrimitives.WriteUInt32BigEndian(e, (uint)names[i].Offset);
            BinaryPrimitives.WriteUInt32BigEndian(e[4..], (uint)names[i].Bytes.Length);
            BinaryPrimitives.WriteUInt64BigEndian(e[8..], (ulong)data[i].Offset);
            BinaryPrimitives.WriteUInt64BigEndian(e[16..], (ulong)data[i].Bytes.Length);
            e[27] = Items[i].Data is null ? (byte)4 : (byte)3;
            names[i].Bytes.CopyTo(enc, names[i].Offset);
            data[i].Bytes.CopyTo(enc, data[i].Offset);
        }
        table.CopyTo(enc, 0);

        var contentKey = Ecb(TestKeys[KeyType - 2], Iv);
        var plainRanges = Items.Select((item, i) => (item, i)).Where(x => x.item.Name == "sce_sys/package/digs.bin")
            .Select(x => (data[x.i].Offset, data[x.i].Bytes.Length)).ToList();
        var encrypted = CtrReference(contentKey, Iv, enc);
        foreach (var (offset, length) in plainRanges) enc.AsSpan(offset, length).CopyTo(encrypted.AsSpan(offset));

        // Metadata after the 0x100-byte header, then the SFO copy, then the encrypted area.
        var sfo = Sfo?.ToArray();
        var meta = new MemoryStream();
        void Entry(uint type, params uint[] values)
        {
            Span<byte> b = stackalloc byte[8 + values.Length * 4];
            BinaryPrimitives.WriteUInt32BigEndian(b, type);
            BinaryPrimitives.WriteUInt32BigEndian(b[4..], (uint)(values.Length * 4));
            for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32BigEndian(b[(8 + i * 4)..], values[i]);
            meta.Write(b);
        }
        const int metaOffset = 0x100;
        Entry(2, ContentType);
        Entry(13, 0, (uint)itemsSize);
        var metaCount = 2u;
        var sfoOffset = Align(metaOffset + (int)meta.Length + (sfo is null ? 0 : 16));
        if (sfo is not null)
        {
            Entry(14, (uint)sfoOffset, (uint)sfo.Length);
            metaCount++;
        }
        var encOffset = Align(sfoOffset + (sfo?.Length ?? 0));
        var file = new byte[encOffset + encrypted.Length + Tail.Length];
        var h = file.AsSpan();
        h[0] = 0x7F; h[1] = (byte)'P'; h[2] = (byte)'K'; h[3] = (byte)'G';
        BinaryPrimitives.WriteUInt16BigEndian(h[4..], 0x8000);
        BinaryPrimitives.WriteUInt16BigEndian(h[6..], 2);
        BinaryPrimitives.WriteUInt32BigEndian(h[8..], metaOffset);
        BinaryPrimitives.WriteUInt32BigEndian(h[12..], metaCount);
        BinaryPrimitives.WriteUInt32BigEndian(h[16..], 0xC0);
        BinaryPrimitives.WriteUInt32BigEndian(h[20..], (uint)Items.Count);
        BinaryPrimitives.WriteUInt64BigEndian(h[24..], (ulong)file.Length);
        BinaryPrimitives.WriteUInt64BigEndian(h[32..], (ulong)encOffset);
        BinaryPrimitives.WriteUInt64BigEndian(h[40..], (ulong)encrypted.Length);
        Encoding.ASCII.GetBytes(ContentId).CopyTo(file, 0x30);
        Iv.CopyTo(file, 0x70);
        BinaryPrimitives.WriteUInt32BigEndian(h[0xC0..], 0x7F657874);
        h[0xE7] = (byte)(0x80 | KeyType);
        meta.ToArray().CopyTo(file, metaOffset);
        sfo?.CopyTo(file, sfoOffset);
        encrypted.CopyTo(file, encOffset);
        Tail.CopyTo(file, encOffset + encrypted.Length);
        HeadSize = encOffset + itemsSize;
        return file;
    }

    public string BuildTo(string path)
    {
        File.WriteAllBytes(path, Build());
        return path;
    }

    private static byte[] Ecb(byte[] key, byte[] block)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptEcb(block, PaddingMode.None);
    }

    /// <summary>CTR the slow, obvious way: counter = IV + block index mod 2^128, big-endian.</summary>
    internal static byte[] CtrReference(byte[] key, byte[] iv, byte[] data, ulong firstBlock = 0)
    {
        var output = new byte[data.Length];
        var start = new BigInteger(iv, isUnsigned: true, isBigEndian: true);
        var modulus = BigInteger.One << 128;
        for (var i = 0; i * 16 < data.Length; i++)
        {
            var counter = ((start + firstBlock + (ulong)i) % modulus).ToByteArray(isUnsigned: true, isBigEndian: true);
            var block = new byte[16];
            counter.CopyTo(block, 16 - counter.Length);
            var stream = Ecb(key, block);
            for (var j = 0; j < 16 && i * 16 + j < data.Length; j++) output[i * 16 + j] = (byte)(data[i * 16 + j] ^ stream[j]);
        }
        return output;
    }
}
