using LibPsVitaKit.Sfo;
using Xunit;

namespace LibPsVitaKit.Tests;

public class SfoTests
{
    internal static SfoFile Sample()
    {
        var sfo = new SfoFile();
        sfo.SetText("TITLE", "Test Game ™", 0x80);
        sfo.SetText("TITLE_ID", "PCSE00000", 0x0C);
        sfo.SetText("CONTENT_ID", "UP0000-PCSE00000_00-0000000000000000", 0x30);
        sfo.SetText("CATEGORY", "gd", 4);
        sfo.SetText("APP_VER", "01.00", 8);
        sfo.SetInt("ATTRIBUTE", 0x02000000);
        return sfo;
    }

    [Fact]
    public void Write_ThenParse_GivesTheSameEntries()
    {
        var back = SfoFile.Parse(Sample().ToArray());
        Assert.Equal("Test Game ™", back.Title);
        Assert.Equal("PCSE00000", back.TitleId);
        Assert.Equal("UP0000-PCSE00000_00-0000000000000000", back.ContentId);
        Assert.Equal("gd", back.Category);
        Assert.Equal("01.00", back.AppVersion);
        Assert.Equal(0x02000000, back.GetInt("ATTRIBUTE"));
        Assert.Equal(0x80, back.Entries["TITLE"].MaxLength);
        Assert.Equal(0x0101u, back.Version);
    }

    [Fact]
    public void Write_IsStable()
    {
        var once = Sample().ToArray();
        Assert.Equal(once, SfoFile.Parse(once).ToArray());
    }

    [Fact]
    public void Write_PadsValuesTo4Bytes()
    {
        var sfo = new SfoFile();
        sfo.SetText("A", "xyz12"); // 6 bytes with the terminator → 8
        var bytes = sfo.ToArray();
        Assert.Equal(8, SfoFile.Parse(bytes).Entries["A"].MaxLength);
        Assert.Equal(0, bytes.Length % 4);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0, (byte)'P', (byte)'S', (byte)'G', 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void Parse_RejectsNonSfo(byte[] bytes) => Assert.Throws<VitaFormatException>(() => SfoFile.Parse(bytes));

    [Fact]
    public void Parse_RejectsTruncatedIndex()
    {
        var bytes = Sample().ToArray();
        bytes[16] = 200; // claims 200 entries
        Assert.Throws<VitaFormatException>(() => SfoFile.Parse(bytes));
    }
}
