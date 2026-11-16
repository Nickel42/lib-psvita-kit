using System.Security.Cryptography;
using LibPsVitaKit.Crypto;
using LibPsVitaKit.Keys;
using LibPsVitaKit.Operations;
using LibPsVitaKit.Pkg;
using Xunit;

namespace LibPsVitaKit.Tests;

/// <summary>.pkg → NoNpDrm folder, on synthetic packages (<see cref="PkgBuilder"/>).</summary>
public sealed class PkgExtractTests : IDisposable
{
    private const string GameId = "UP0000-PCSE00000_00-0000000000000000";
    private const string DlcId = "EP9000-PCSF00001_00-ADDCONT000000002";
    private const string PsmId = "UP0001-NPNA00000_00-0000000000000001";

    private readonly string _dir = Directory.CreateTempSubdirectory("lib-psvita-kit-").FullName;
    private readonly byte[] _eboot = PkgBuilder.Pattern(1_500_000, 1); // more than one 1 MiB chunk
    private readonly byte[] _digs = PkgBuilder.Pattern(100, 2);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Out => Path.Combine(_dir, "out");
    private static VitaConverter Converter(PkgKeys? keys = null) => new(keys ?? PkgBuilder.Keys());
    private static string Sha256(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private PkgBuilder Game(string contentId = GameId, uint contentType = 0x15, string category = "gd")
    {
        var b = new PkgBuilder { ContentId = contentId, ContentType = contentType, Sfo = PkgBuilder.GameSfo(contentId, category) };
        b.Items.Add(("sce_sys", null));
        b.Items.Add(("sce_sys/param.sfo", b.Sfo.ToArray()));
        b.Items.Add(("sce_sys/package", null));
        b.Items.Add(("sce_sys/package/digs.bin", _digs));
        b.Items.Add(("eboot.bin", _eboot));
        b.Items.Add(("data/level1.bin", PkgBuilder.Pattern(33, 3))); // folder made from the file's path
        return b;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Game_WithZrif_BecomesALicensedNoNpDrmFolder(int keyType)
    {
        var builder = Game();
        builder.KeyType = keyType;
        var pkg = builder.BuildTo(Path.Combine(_dir, "game.pkg"));
        var bytes = File.ReadAllBytes(pkg);

        var result = await Converter().ExtractPkgAsync(pkg, ZrifVectors.VitaZrif, Out);

        var root = Path.Combine(Out, "app", "PCSE00000");
        Assert.Equal(root, result.OutputPath);
        Assert.Equal(PkgContentKind.VitaApp, result.Kind);
        Assert.True(result.Licensed);
        Assert.Empty(result.Warnings);
        Assert.Equal(_eboot, File.ReadAllBytes(Path.Combine(root, "eboot.bin")));
        Assert.Equal(33, new FileInfo(Path.Combine(root, "data", "level1.bin")).Length);
        Assert.Equal(_digs, File.ReadAllBytes(Path.Combine(root, "sce_sys", "package", "body.bin")));
        Assert.False(File.Exists(Path.Combine(root, "sce_sys", "package", "digs.bin")));
        Assert.Equal(ZrifVectors.VitaRifSha256, Sha256(File.ReadAllBytes(Path.Combine(root, "sce_sys", "package", "work.bin"))));
        Assert.Equal(bytes[..(int)builder.HeadSize], File.ReadAllBytes(Path.Combine(root, "sce_sys", "package", "head.bin")));
        Assert.Equal(builder.Tail, File.ReadAllBytes(Path.Combine(root, "sce_sys", "package", "tail.bin")));
        Assert.Equal(new byte[768], File.ReadAllBytes(Path.Combine(root, "sce_sys", "package", "stat.bin")));

        var info = VitaFile.Inspect(root);
        Assert.Equal(VitaFileType.NoNpDrmFolder, info.Type);
        Assert.Equal(("Test Game", "PCSE00000"), (info.Title, info.TitleId));
        Assert.Empty(Directory.GetDirectories(Out, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Game_WithoutZrif_IsUnpackedWithAWarning()
    {
        var pkg = Game().BuildTo(Path.Combine(_dir, "game.pkg"));
        var result = await Converter().ExtractPkgAsync(pkg, null, Out);
        Assert.False(result.Licensed);
        Assert.Contains(result.Warnings, w => w.Contains("work.bin"));
        Assert.False(File.Exists(Path.Combine(result.OutputPath, "sce_sys", "package", "work.bin")));
        Assert.True(File.Exists(Path.Combine(result.OutputPath, "eboot.bin")));
    }

    [Fact]
    public async Task OpenedPackage_WithLargeAndEmptyFiles_IsUnpackedExactly()
    {
        // 3.25 chunks: reads run ahead of the decrypt and write; the empty file has nothing to read.
        var big = PkgBuilder.Pattern(3 * (1 << 20) + (1 << 18) + 5, 7);
        var b = Game();
        b.Items.Add(("data/big.psarc", big));
        b.Items.Add(("data/empty.dat", []));
        var pkg = PkgFile.Open(b.BuildTo(Path.Combine(_dir, "game.pkg")));

        var result = await Converter().ExtractPkgAsync(pkg, ZrifVectors.VitaZrif, Out);

        Assert.Equal(big, File.ReadAllBytes(Path.Combine(result.OutputPath, "data", "big.psarc")));
        Assert.Empty(File.ReadAllBytes(Path.Combine(result.OutputPath, "data", "empty.dat")));
        Assert.Equal(_eboot, File.ReadAllBytes(Path.Combine(result.OutputPath, "eboot.bin")));
        Assert.Equal(b.Tail, File.ReadAllBytes(Path.Combine(result.OutputPath, "sce_sys", "package", "tail.bin")));
    }

    [Fact]
    public async Task Game_WithAnotherGamesZrif_IsRefused_AndLeavesNothing()
    {
        var pkg = Game().BuildTo(Path.Combine(_dir, "game.pkg"));
        var ex = await Assert.ThrowsAsync<MissingLicenseException>(() => Converter().ExtractPkgAsync(pkg, ZrifVectors.VitaDlcZrif, Out));
        Assert.Equal(GameId, ex.ContentId);
        Assert.False(Directory.Exists(Out));
        await Assert.ThrowsAsync<MissingLicenseException>(() => Converter().ExtractPkgAsync(pkg, ZrifVectors.PsmZrif, Out));
    }

    [Fact]
    public async Task Update_GoesUnderPatch_AndIgnoresTheZrif()
    {
        var pkg = Game(category: "gp").BuildTo(Path.Combine(_dir, "patch.pkg"));
        var result = await Converter().ExtractPkgAsync(pkg, ZrifVectors.VitaZrif, Out);
        Assert.Equal(PkgContentKind.VitaPatch, result.Kind);
        Assert.Equal(Path.Combine(Out, "patch", "PCSE00000"), result.OutputPath);
        Assert.False(result.Licensed);
        Assert.Contains(result.Warnings, w => w.Contains("ignored"));
        Assert.True(File.Exists(Path.Combine(result.OutputPath, "sce_sys", "package", "head.bin")));
    }

    [Fact]
    public async Task Dlc_GoesUnderAddcont_WithItsLabel()
    {
        var pkg = Game(DlcId, contentType: 0x16, category: "ac").BuildTo(Path.Combine(_dir, "dlc.pkg"));
        var result = await Converter().ExtractPkgAsync(pkg, ZrifVectors.VitaDlcZrif, Out);
        Assert.Equal(PkgContentKind.VitaDlc, result.Kind);
        Assert.Equal(Path.Combine(Out, "addcont", "PCSF00001", "ADDCONT000000002"), result.OutputPath);
        Assert.Equal(ZrifVectors.VitaDlcRifSha256, Sha256(File.ReadAllBytes(Path.Combine(result.OutputPath, "sce_sys", "package", "work.bin"))));
    }

    [Fact]
    public async Task Psm_GoesUnderPsm_WithRoAndRw()
    {
        var b = new PkgBuilder { ContentId = PsmId, ContentType = 0x18 };
        var app = PkgBuilder.Pattern(5000, 4);
        b.Items.Add(("content", null));
        b.Items.Add(("content/Application", null));
        b.Items.Add(("content/Application/app.exe", app));
        var pkg = b.BuildTo(Path.Combine(_dir, "psm.pkg"));

        var result = await Converter().ExtractPkgAsync(pkg, ZrifVectors.PsmZrif, Out);

        var root = Path.Combine(Out, "psm", "NPNA00000");
        Assert.Equal(root, result.OutputPath);
        Assert.Equal(PkgContentKind.Psm, result.Kind);
        Assert.Equal(app, File.ReadAllBytes(Path.Combine(root, "RO", "Application", "app.exe")));
        Assert.Equal(ZrifVectors.PsmRifSha256, Sha256(File.ReadAllBytes(Path.Combine(root, "RO", "License", "FAKE.rif"))));
        var contentId = File.ReadAllBytes(Path.Combine(root, "RW", "System", "content_id"));
        Assert.Equal(0x30, contentId.Length);
        Assert.Equal(PsmId, System.Text.Encoding.ASCII.GetString(contentId).TrimEnd('\0'));
        Assert.Equal(0x10000, new FileInfo(Path.Combine(root, "RW", "System", "pm.dat")).Length);
        Assert.True(Directory.Exists(Path.Combine(root, "RW", "Documents")));
        Assert.True(Directory.Exists(Path.Combine(root, "RW", "Temp")));
        Assert.False(Directory.Exists(Path.Combine(root, "sce_sys")));
    }

    [Fact]
    public async Task MissingKey_IsNamed()
    {
        var builder = Game();
        builder.KeyType = 3;
        var pkg = builder.BuildTo(Path.Combine(_dir, "game.pkg"));
        var ex = await Assert.ThrowsAsync<MissingKeyException>(() => Converter(new PkgKeys().Set(2, PkgBuilder.TestKeys[0])).ExtractPkgAsync(pkg, null, Out));
        Assert.Equal("pkg_vita_3", ex.KeyName);
    }

    [Fact]
    public async Task WrongKey_IsCaughtByTheItemTableChecks()
    {
        var pkg = Game().BuildTo(Path.Combine(_dir, "game.pkg"));
        var wrong = new PkgKeys().Set(2, PkgBuilder.TestKeys[1]);
        await Assert.ThrowsAsync<VitaFormatException>(() => Converter(wrong).ExtractPkgAsync(pkg, null, Out));
    }

    [Fact]
    public async Task ExistingOutput_NeedsOverwrite()
    {
        var pkg = Game().BuildTo(Path.Combine(_dir, "game.pkg"));
        var first = await Converter().ExtractPkgAsync(pkg, null, Out);
        File.WriteAllText(Path.Combine(first.OutputPath, "stale.txt"), "old");
        await Assert.ThrowsAsync<IOException>(() => Converter().ExtractPkgAsync(pkg, null, Out));
        var second = await Converter().ExtractPkgAsync(pkg, ZrifVectors.VitaZrif, Out, new VitaConvertOptions { Overwrite = true });
        Assert.False(File.Exists(Path.Combine(second.OutputPath, "stale.txt")));
        Assert.True(second.Licensed);
    }

    [Theory]
    [InlineData("../evil.bin")]
    [InlineData("/abs.bin")]
    [InlineData("a//b.bin")]
    [InlineData("C:x.bin")]
    [InlineData("sce_sys\\x.bin")]
    public async Task UnsafeNames_AreRefused(string name)
    {
        var b = Game();
        b.Items.Add((name, [1, 2, 3]));
        var pkg = b.BuildTo(Path.Combine(_dir, "evil.pkg"));
        await Assert.ThrowsAsync<VitaFormatException>(() => Converter().ExtractPkgAsync(pkg, null, Out));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.bin")));
    }

    [Fact]
    public async Task Cancellation_LeavesNoOutput()
    {
        var pkg = Game().BuildTo(Path.Combine(_dir, "game.pkg"));
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.Fraction > 0) cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Converter().ExtractPkgAsync(pkg, null, Out, progress: progress, cancellationToken: cts.Token));
        Assert.False(Directory.Exists(Path.Combine(Out, "app", "PCSE00000")));
        Assert.Empty(Directory.GetDirectories(Out, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Inspect_ReadsAPackagesTitleWithoutKeys()
    {
        var pkg = Game().BuildTo(Path.Combine(_dir, "game.pkg"));
        var info = VitaFile.Inspect(pkg);
        Assert.Equal(VitaFileType.Pkg, info.Type);
        Assert.Equal(PkgContentKind.VitaApp, info.PkgKind);
        Assert.Equal(("Test Game", "PCSE00000", GameId), (info.Title, info.TitleId, info.ContentId));
    }

    [Fact]
    public async Task PspPackage_IsRecognisedButNotUnpacked()
    {
        var b = Game(contentType: 0x07);
        b.Sfo = null;
        var pkg = b.BuildTo(Path.Combine(_dir, "psp.pkg"));
        Assert.Equal(PkgContentKind.Psp, PkgFile.Open(pkg).Kind);
        await Assert.ThrowsAsync<VitaFormatException>(() => Converter().ExtractPkgAsync(pkg, null, Out));
    }

    [Theory]
    [InlineData(0ul, 1)]
    [InlineData(0ul, 100)]
    [InlineData(1ul, 37)]   // carries out of the IV's low bytes (it ends FFFE)
    [InlineData(0x1_0000_0000ul, 64)]
    public void AesCtr_MatchesTheReference(ulong block, int length)
    {
        var key = PkgBuilder.TestKeys[0];
        var iv = Convert.FromHexString("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFE"); // wraps around 2^128
        var data = PkgBuilder.Pattern(length, 9);
        var ours = (byte[])data.Clone();
        using (var aes = AesCtr.Create(key)) AesCtr.Xor(aes, iv, block, ours);
        Assert.Equal(PkgBuilder.CtrReference(key, iv, data, block), ours);
    }

    [Theory]
    [InlineData("00000000000000FFFFFFFFFFFFFFFFF0", 0ul, 4_000)]       // carries from the low 64 bits into the high
    [InlineData("A0A1A2A3A4A5A6A7A8A9AAABACADFFFE", 3ul, 200_003)]     // several 64 KiB key-stream windows and a ragged end
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFF000", 0ul, 1 << 20)]     // a whole chunk that wraps around 2^128
    public void AesCtr_LongRuns_MatchTheReference(string ivHex, ulong block, int length)
    {
        var key = PkgBuilder.TestKeys[1];
        var iv = Convert.FromHexString(ivHex);
        var data = PkgBuilder.Pattern(length, 10);
        var ours = (byte[])data.Clone();
        using (var aes = AesCtr.Create(key)) AesCtr.Xor(aes, iv, block, ours);
        Assert.Equal(PkgBuilder.CtrReference(key, iv, data, block), ours);
    }

    [Fact]
    public void ReadItems_NamesReadTogetherOrOneByOne_Agree()
    {
        var pkg = PkgFile.Open(Game().BuildTo(Path.Combine(_dir, "game.pkg")));
        var key = PkgBuilder.Keys().ContentKey(pkg.Header);
        var together = pkg.ReadItems(key);
        Assert.Equal(["sce_sys", "sce_sys/param.sfo", "sce_sys/package", "sce_sys/package/digs.bin", "eboot.bin", "data/level1.bin"],
            together.Select(i => i.Name));
        Assert.Equal(together, pkg.ReadItems(key, maxNameSpan: 0));
    }

    [Fact]
    public void Keys_ParseFromText()
    {
        var keys = PkgKeys.Parse("# test\npkg_vita_2 = 00112233445566778899AABBCCDDEEF0\nPKG_VITA_4: f0e1d2c3 b4a59687 78695a4b 3c2d1e0f\nother = 12\n");
        Assert.True(keys.Has(2));
        Assert.False(keys.Has(3));
        Assert.True(keys.Has(4));
        Assert.Throws<FormatException>(() => PkgKeys.Parse("pkg_vita_3 = 1234"));
    }

    [Fact]
    public void BuiltInKeys_CoverEveryVitaKeyType()
    {
        var keys = PkgKeys.BuiltIn();
        Assert.All(PkgKeys.KeyNames.Keys, type => Assert.True(keys.Has(type)));
    }

    [Fact]
    public async Task KeyFile_OverridesTheBuiltInKeys()
    {
        // The synthetic package uses a made-up key 2, so the built-in key 2 can't open it until a file overrides it.
        var pkg = Game().BuildTo(Path.Combine(_dir, "game.pkg"));
        await Assert.ThrowsAsync<VitaFormatException>(() => new VitaConverter().ExtractPkgAsync(pkg, null, Out));
        var keyFile = Path.Combine(_dir, "keys.txt");
        File.WriteAllText(keyFile, $"pkg_vita_2 = {Convert.ToHexString(PkgBuilder.TestKeys[0])}\n");
        var keys = PkgKeys.BuiltInWith(keyFile);
        Assert.True(keys.Has(3) && keys.Has(4));
        var result = await new VitaConverter(keys).ExtractPkgAsync(pkg, null, Out);
        Assert.True(File.Exists(Path.Combine(result.OutputPath, "eboot.bin")));
    }

    /// <summary>Progress&lt;T&gt; posts to the thread pool; this reports in line, so cancelling is deterministic.</summary>
    private sealed class SyncProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
