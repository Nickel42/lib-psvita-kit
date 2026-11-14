using LibPsVitaKit.Ids;
using Xunit;

namespace LibPsVitaKit.Tests;

public class IdTests
{
    [Theory]
    [InlineData("PCSE00000", true)]
    [InlineData("PCSB01234", true)]
    [InlineData("NPXS10000", true)]
    [InlineData("pcse00000", false)]
    [InlineData("PCSE0000", false)]
    [InlineData("PCSE0000A", false)]
    [InlineData("", false)]
    public void TitleId_Validates(string s, bool valid) => Assert.Equal(valid, TitleId.TryParse(s, out _));

    [Fact]
    public void ContentId_SplitsIntoParts()
    {
        var id = ContentId.Parse("EP9000-PCSF00001_00-GAMEDATA00000001\0\0");
        Assert.Equal("EP9000", id.Publisher);
        Assert.Equal("PCSF00001", id.TitleId.Value);
        Assert.Equal("GAMEDATA00000001", id.Label);
    }

    [Theory]
    [InlineData("UP0000-PCSE00000-00-0000000000000000")]
    [InlineData("UP0000_PCSE00000_00-0000000000000000")]
    [InlineData("UP0000-PCSE0000X_00-0000000000000000")]
    [InlineData("UP0000-PCSE00000_00-000")]
    public void ContentId_RejectsMalformed(string s) => Assert.False(ContentId.TryParse(s, out _));
}
