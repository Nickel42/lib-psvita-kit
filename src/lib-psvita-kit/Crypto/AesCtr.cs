using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace LibPsVitaKit.Crypto;

/// <summary>
/// AES-128-CTR as PlayStation packages use it: the counter is the package IV plus the 16-byte block
/// index, added as one 128-bit big-endian number. Built on ECB, which every .NET platform has.
/// </summary>
internal static class AesCtr
{
    /// <summary>Key stream made per pass: small enough to stay in cache while it is XORed in.</summary>
    private const int Window = 64 * 1024;

    /// <summary>XORs <paramref name="data"/> with the key stream starting at block <paramref name="block"/>.</summary>
    public static void Xor(Aes aes, ReadOnlySpan<byte> iv, ulong block, Span<byte> data)
    {
        if (data.IsEmpty) return;
        // UInt128 wraps at 2^128 just as the byte-wise big-endian add did.
        var counter = BinaryPrimitives.ReadUInt128BigEndian(iv[..16]) + block;
        var size = Math.Min(Window, (data.Length + 15) & ~15);
        var rented = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            while (!data.IsEmpty)
            {
                var part = data[..Math.Min(Window, data.Length)];
                var stream = rented.AsSpan(0, (part.Length + 15) & ~15);
                for (var i = 0; i < stream.Length; i += 16) BinaryPrimitives.WriteUInt128BigEndian(stream[i..], counter++);
                aes.EncryptEcb(stream, stream, PaddingMode.None); // in place: counters become key stream
                XorInto(part, stream);
                data = data[part.Length..];
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void XorInto(Span<byte> data, ReadOnlySpan<byte> stream)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= data.Length - Vector<byte>.Count; i += Vector<byte>.Count)
                (new Vector<byte>(data[i..]) ^ new Vector<byte>(stream[i..])).CopyTo(data[i..]);
        }
        for (; i < data.Length; i++) data[i] ^= stream[i];
    }

    /// <summary>One AES-128-ECB block: how a package's key is made from its IV.</summary>
    public static byte[] EncryptBlock(ReadOnlySpan<byte> key, ReadOnlySpan<byte> block)
    {
        using var aes = Create(key);
        return aes.EncryptEcb(block[..16], PaddingMode.None);
    }

    public static Aes Create(ReadOnlySpan<byte> key)
    {
        var aes = Aes.Create();
        aes.Key = key.ToArray();
        return aes;
    }
}
