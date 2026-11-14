using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using LibPsVitaKit.Pkg;
using Xunit;

namespace LibPsVitaKit.Tests;

/// <summary>Recognising each kind of file, built synthetically in a temp folder.</summary>
public sealed class DetectTests : IDisposable
{
    private const string ContentId = "UP0000-PCSE00000_00-0000000000000000";
    private readonly string _dir = Directory.CreateTempSubdirectory("lib-psvita-kit-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Path_(string name) => Path.Combine(_dir, name);

    internal static byte[] FakePkgHeader()
    {
        var h = new byte[PkgHeader.ReadSize];
        h[0] = 0x7F; h[1] = (byte)'P'; h[2] = (byte)'K'; h[3] = (byte)'G';
        BinaryPrimitives.WriteUInt16BigEndian(h.AsSpan(4), 0x8000);
        BinaryPrimitives.WriteUInt16BigEndian(h.AsSpan(6), 2);
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(0x14), 7);
        BinaryPrimitives.WriteUInt64BigEndian(h.AsSpan(0x18), 0x123456789);
        BinaryPrimitives.WriteUInt64BigEndian(h.AsSpan(0x20), 0x2000);
        Encoding.ASCII.GetBytes(ContentId).CopyTo(h, 0x30);
        h[0x70] = 0xAB;
        h[0xE7] = 0x0A; // key type 2
        return h;
    }

    [Fact]
    public void Pkg_IsRecognised_AndItsHeaderRead()
    {
        var path = Path_("game.pkg");
        File.WriteAllBytes(path, FakePkgHeader());
        Assert.Equal(VitaFileType.Pkg, VitaFile.Detect(path));
        var h = PkgHeader.Read(path);
        Assert.Equal(ContentId, h.ContentId);
        Assert.Equal(7u, h.ItemCount);
        Assert.Equal(0x123456789ul, h.TotalSize);
        Assert.Equal(0x2000ul, h.DataOffset);
        Assert.Equal(0xAB, h.Iv[0]);
        Assert.Equal(2, h.KeyType);
        var info = VitaFile.Inspect(path);
        Assert.Equal("PCSE00000", info.TitleId);
    }

    [Fact]
    public void Folder_IsRecognised_FromItsParamSfo()
    {
        var game = Directory.CreateDirectory(Path_("PCSE00000/sce_sys")).Parent!.FullName;
        SfoTests.Sample().Save(Path.Combine(game, "sce_sys", "param.sfo"));
        var info = VitaFile.Inspect(game);
        Assert.Equal(VitaFileType.NoNpDrmFolder, info.Type);
        Assert.Equal("Test Game ™", info.Title);
        Assert.Equal(VitaFileType.Unknown, VitaFile.Detect(_dir));
    }

    [Theory]
    [InlineData("", "eboot.bin", VitaFileType.Vpk)]
    [InlineData("PCSE00000/", "PCSE00000/eboot.bin", VitaFileType.NoNpDrmZip)]
    [InlineData("app/PCSE00000/", "app/PCSE00000/eboot.bin", VitaFileType.NoNpDrmZip)]
    [InlineData("NotATitle/", "NotATitle/eboot.bin", VitaFileType.Unknown)]
    public void Zip_IsRecognised_ByWhereItsParamSfoIs(string prefix, string eboot, VitaFileType expected)
    {
        var path = Path_("game.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var s = zip.CreateEntry(prefix + "sce_sys/param.sfo").Open()) SfoTests.Sample().Write(s);
            zip.CreateEntry(eboot);
        }
        var info = VitaFile.Inspect(path);
        Assert.Equal(expected, info.Type);
        if (expected != VitaFileType.Unknown) Assert.Equal("PCSE00000", info.TitleId);
    }

    [Fact]
    public void BareSfo_AndOtherFiles()
    {
        var sfo = Path_("param.sfo");
        SfoTests.Sample().Save(sfo);
        Assert.Equal(VitaFileType.Sfo, VitaFile.Detect(sfo));
        var other = Path_("other.bin");
        File.WriteAllBytes(other, [1, 2, 3, 4, 5]);
        Assert.Equal(VitaFileType.Unknown, VitaFile.Detect(other));
        Assert.Throws<FileNotFoundException>(() => VitaFile.Detect(Path_("missing")));
    }

    [Fact]
    public void PkgHeader_RejectsShortOrForeignData()
    {
        Assert.Throws<VitaFormatException>(() => PkgHeader.Parse(FakePkgHeader().AsSpan(0, 0x40)));
        Assert.Throws<VitaFormatException>(() => PkgHeader.Parse(new byte[PkgHeader.ReadSize]));
    }
}
