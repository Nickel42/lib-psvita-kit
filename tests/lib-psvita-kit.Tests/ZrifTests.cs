using System.Security.Cryptography;
using LibPsVitaKit.Licenses;
using Xunit;

namespace LibPsVitaKit.Tests;

/// <summary>
/// zRIF strings made by Python's zlib the way pkg2zip's rif2zrif.py makes them
/// (reference/python/gen_zrif_vectors.py), from made-up RIFs.
/// </summary>
internal static class ZrifVectors
{
    public const string DictionarySha256 = "71558a9c2d0dd3992e009d5687b5505a76d9fdc60d16bfe802f5dab5608fbbb5";
    public const int DictionaryLength = 1024;
    public const string VitaZrif = "KO5ifR1dA/OQQGiAAVS1K9gCkP0GaACf/y5FN51nMv7/ZW1q44T5nDqNcVeWLAm5M2XLnm+7mzmTr04kJD+aQugLAPzTK0AA";
    public const string VitaRifSha256 = "011d3290b22738bd6b75792450a812383216fe055ac458f3bb19763d00c7a37d";
    public const string VitaDlcZrif = "KO5ifR1dA/OQgGuAJZJpBoZo9oPdgM9/9+vCJ++pepgnr++0Xoy53C2l3GqaimaJS+z/z+UnmWYcISQ/mkLoCwDRmiVC";
    public const string VitaDlcRifSha256 = "b8dae59ec8788e47142ed4966a12a08598cc9ee867a6a9b3bd1b85f645f1e3f4";
    public const string PsmZrif = "KO5ifR1dA/OoCEIDwK7yC/BzBDsQ5H4DVGCIT7/Np/oSjtKoOKENHUKbtG70bCxW2qqV+PjX3smfXr5/H5ZBSH40hY2CUUA8AADSaynY";
    public const string PsmRifSha256 = "b7cac9b6a8f3c5f924bb1258981bc1bfd7e3a29363ce4e34eb896c5eb84d8b03";
}

public class ZrifTests
{
    private static string Sha256(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [Fact]
    public void Dictionary_IsPkg2zips()
    {
        Assert.Equal(ZrifVectors.DictionaryLength, Zrif.Dictionary.Length);
        Assert.Equal(Zrif.DictionaryId, Zrif.Adler32(Zrif.Dictionary));
        Assert.Equal(ZrifVectors.DictionarySha256, Sha256(Zrif.Dictionary));
    }

    [Theory]
    [InlineData(ZrifVectors.VitaZrif, ZrifVectors.VitaRifSha256, Zrif.VitaRifSize, "UP0000-PCSE00000_00-0000000000000000")]
    [InlineData(ZrifVectors.VitaDlcZrif, ZrifVectors.VitaDlcRifSha256, Zrif.VitaRifSize, "EP9000-PCSF00001_00-ADDCONT000000002")]
    [InlineData(ZrifVectors.PsmZrif, ZrifVectors.PsmRifSha256, Zrif.PsmRifSize, "UP0001-NPNA00000_00-0000000000000001")]
    public void Decode_GivesTheRif(string zrif, string sha256, int size, string contentId)
    {
        var rif = Zrif.Decode(zrif);
        Assert.Equal(size, rif.Length);
        Assert.Equal(sha256, Sha256(rif));
        Assert.Equal(contentId, Zrif.ContentIdOf(rif));
    }

    [Fact]
    public void Decode_AcceptsMissingPaddingAndSurroundingSpace()
    {
        var expected = Zrif.Decode(ZrifVectors.PsmZrif);
        var bare = ZrifVectors.PsmZrif.TrimEnd('=');
        Assert.Equal(expected, Zrif.Decode("  " + bare + "\n"));
    }

    [Fact]
    public void Decode_AcceptsAStreamWithoutTheDictionary()
    {
        // A zlib stream with no FDICT flag: header 78 01, one stored block, Adler-32.
        var rif = new byte[Zrif.VitaRifSize];
        "UP0000-PCSE00000_00-0000000000000000"u8.CopyTo(rif.AsSpan(0x10));
        var adler = Zrif.Adler32(rif);
        byte[] stream = [0x78, 0x01, 0x01, 0x00, 0x02, 0xFF, 0xFD, .. rif,
            (byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler];
        Assert.Equal(rif, Zrif.Decode(Convert.ToBase64String(stream)));
    }

    [Theory]
    [InlineData("not base64!!")]
    [InlineData("KO5i")]                 // too short
    [InlineData("AAAAAAAAAAAA")]         // not deflate
    [InlineData("eAAAAAAAAAAA")]         // header check fails
    [InlineData("KO5ifR1eA/OQQGiAAVS1")] // unknown dictionary ID
    public void Decode_RejectsBadInput(string zrif)
    {
        Assert.Throws<VitaFormatException>(() => Zrif.Decode(zrif));
        Assert.False(Zrif.TryDecode(zrif, out _));
    }

    [Fact]
    public void Decode_RejectsATruncatedOrAlteredStream()
    {
        var raw = Convert.FromBase64String(ZrifVectors.VitaZrif);
        Assert.Throws<VitaFormatException>(() => Zrif.Decode(Convert.ToBase64String(raw[..^8])));
        raw[^5] ^= 0xFF; // inside the Adler-32 trailer
        Assert.Throws<VitaFormatException>(() => Zrif.Decode(Convert.ToBase64String(raw)));
    }

    [Fact]
    public void ContentIdOf_RejectsOtherSizes() => Assert.Throws<VitaFormatException>(() => Zrif.ContentIdOf(new byte[100]));

    [Fact]
    public void Encode_IsNotImplementedYet() => Assert.Throws<NotImplementedException>(() => Zrif.Encode(new byte[Zrif.VitaRifSize]));
}
