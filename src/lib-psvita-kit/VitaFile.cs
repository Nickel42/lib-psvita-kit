using System.IO.Compression;
using LibPsVitaKit.Ids;
using LibPsVitaKit.Pkg;
using LibPsVitaKit.Sfo;

namespace LibPsVitaKit;

/// <summary>The kinds of PS Vita file the kit recognises.</summary>
public enum VitaFileType
{
    Unknown,
    /// <summary>A PlayStation Store package (.pkg), encrypted.</summary>
    Pkg,
    /// <summary>A homebrew installer: a zip with eboot.bin and sce_sys/param.sfo at its root.</summary>
    Vpk,
    /// <summary>A decrypted game folder (sce_sys/param.sfo, and sce_sys/package/work.bin when licensed).</summary>
    NoNpDrmFolder,
    /// <summary>A zip holding a NoNpDrm game folder, as Vita3K installs with File › Install .zip.</summary>
    NoNpDrmZip,
    /// <summary>A bare param.sfo.</summary>
    Sfo,
}

/// <summary>What <see cref="VitaFile.Inspect"/> found. Fields the file doesn't hold are null.</summary>
public sealed record VitaFileInfo(VitaFileType Type, string? Title, string? TitleId, string? ContentId, string? Category, string? AppVersion);

/// <summary>Recognising PS Vita files and reading their identity, from headers only.</summary>
public static class VitaFile
{
    private const string SfoPath = "sce_sys/param.sfo";

    public static VitaFileType Detect(string path)
    {
        if (Directory.Exists(path))
            return File.Exists(Path.Combine(path, "sce_sys", "param.sfo")) ? VitaFileType.NoNpDrmFolder : VitaFileType.Unknown;
        if (!File.Exists(path)) throw new FileNotFoundException("No such file or folder.", path);

        Span<byte> magic = stackalloc byte[4];
        using (var fs = File.OpenRead(path))
            if (fs.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4) return VitaFileType.Unknown;
        if (PkgHeader.HasMagic(magic)) return VitaFileType.Pkg;
        if (magic.SequenceEqual("\0PSF"u8)) return VitaFileType.Sfo;
        if (magic.SequenceEqual("PK\u0003\u0004"u8)) return DetectZip(path).Type;
        return VitaFileType.Unknown;
    }

    /// <exception cref="VitaFormatException">A Vita file whose header can't be read.</exception>
    public static VitaFileInfo Inspect(string path)
    {
        var type = Detect(path);
        switch (type)
        {
            case VitaFileType.Pkg:
                var contentId = PkgHeader.Read(path).ContentId;
                return new(type, null, Ids.ContentId.TryParse(contentId, out var c) ? c.TitleId.Value : null, contentId, null, null);
            case VitaFileType.Sfo:
                return FromSfo(type, SfoFile.Load(path));
            case VitaFileType.NoNpDrmFolder:
                return FromSfo(type, SfoFile.Load(Path.Combine(path, "sce_sys", "param.sfo")));
            case VitaFileType.Vpk or VitaFileType.NoNpDrmZip:
                using (var zip = ZipFile.OpenRead(path))
                using (var s = DetectZip(zip).Sfo!.Open())
                    return FromSfo(type, SfoFile.Read(s));
            default:
                return new(type, null, null, null, null, null);
        }
    }

    private static VitaFileInfo FromSfo(VitaFileType type, SfoFile sfo) =>
        new(type, sfo.Title, sfo.TitleId, sfo.ContentId, sfo.Category, sfo.AppVersion);

    private static (VitaFileType Type, ZipArchiveEntry? Sfo) DetectZip(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return (DetectZip(zip).Type, null);
        }
        catch (InvalidDataException)
        {
            return (VitaFileType.Unknown, null);
        }
    }

    /// <summary>
    /// A .vpk has eboot.bin and sce_sys/param.sfo at its root. A NoNpDrm zip has them one folder
    /// down (TITLEID/...), or under app/TITLEID/ as on a memory card.
    /// </summary>
    private static (VitaFileType Type, ZipArchiveEntry? Sfo) DetectZip(ZipArchive zip)
    {
        ZipArchiveEntry? Find(string name) =>
            zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(name, StringComparison.OrdinalIgnoreCase));

        if (Find(SfoPath) is { } root && Find("eboot.bin") is not null) return (VitaFileType.Vpk, root);
        foreach (var e in zip.Entries)
        {
            var parts = e.FullName.Replace('\\', '/').Split('/');
            var folder = parts.Length == 3 ? parts[0] : parts.Length == 4 && parts[0].Equals("app", StringComparison.OrdinalIgnoreCase) ? parts[1] : null;
            if (folder is not null && TitleId.IsValid(folder) && string.Join('/', parts[^2..]).Equals(SfoPath, StringComparison.OrdinalIgnoreCase))
                return (VitaFileType.NoNpDrmZip, e);
        }
        return (VitaFileType.Unknown, null);
    }
}
