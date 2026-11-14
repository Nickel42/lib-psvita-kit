using System.Buffers;
using System.Security.Cryptography;

namespace LibPsVitaKit.Crypto;

/// <summary>
/// AES-128-CTR as PlayStation packages use it: the counter is the package IV plus the 16-byte block
/// index, added as one 128-bit big-endian number. Built on ECB, which every .NET platform has.
/// </summary>
internal static class AesCtr
{
    /// <summary>XORs <paramref name="data"/> with the key stream starting at block <paramref name="block"/>.</summary>
    public static void Xor(Aes aes, ReadOnlySpan<byte> iv, ulong block, Span<byte> data)
    {
        if (data.IsEmpty) return;
        var blocks = (data.Length + 15) / 16;
        var counters = ArrayPool<byte>.Shared.Rent(blocks * 16);
        var stream = ArrayPool<byte>.Shared.Rent(blocks * 16);
        try
        {
            Span<byte> counter = stackalloc byte[16];
            iv[..16].CopyTo(counter);
            Add(counter, block);
            for (var i = 0; i < blocks; i++)
            {
                counter.CopyTo(counters.AsSpan(i * 16));
                Add(counter, 1);
            }
            aes.EncryptEcb(counters.AsSpan(0, blocks * 16), stream.AsSpan(0, blocks * 16), PaddingMode.None);
            for (var i = 0; i < data.Length; i++) data[i] ^= stream[i];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(counters);
            ArrayPool<byte>.Shared.Return(stream);
        }
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

    private static void Add(Span<byte> counter, ulong n)
    {
        for (var i = 15; i >= 0 && n != 0; i--)
        {
            n += counter[i];
            counter[i] = (byte)n;
            n >>= 8;
        }
    }
}
