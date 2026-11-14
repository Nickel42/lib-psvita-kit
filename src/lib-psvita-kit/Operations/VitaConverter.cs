using System.Text;
using LibPsVitaKit.Crypto;
using LibPsVitaKit.Keys;
using LibPsVitaKit.Licenses;
using LibPsVitaKit.Pkg;

namespace LibPsVitaKit.Operations;

/// <summary>What an operation is doing and how far along it is.</summary>
public readonly record struct OperationProgress(string Stage, double Fraction);

/// <summary>Options shared by the conversions.</summary>
public sealed record VitaConvertOptions
{
    /// <summary>Replace an existing output instead of failing.</summary>
    public bool Overwrite { get; init; }
}

/// <summary>The outcome of a conversion.</summary>
public sealed record VitaOperationResult(string OutputPath, VitaFileType OutputType, IReadOnlyList<string> Warnings)
{
    /// <summary>What the package held.</summary>
    public PkgContentKind Kind { get; init; }
    public string? ContentId { get; init; }
    /// <summary>Whether a license (work.bin, or FAKE.rif for PSM) was written.</summary>
    public bool Licensed { get; init; }
}

/// <summary>
/// Conversions between PS Vita formats. Done: .pkg (+ zRIF) → NoNpDrm folder, for games, DLC,
/// updates and PSM titles. Planned (docs/SPEC.md): NoNpDrm folder ↔ .vpk and zip.
/// </summary>
public sealed class VitaConverter(PkgKeys keys)
{
    /// <summary>A converter with the built-in package keys (<see cref="PkgKeys.BuiltIn"/>).</summary>
    public VitaConverter() : this(PkgKeys.BuiltIn()) { }

    private const string TempSuffix = ".lib-psvita-kit.tmp";
    private const int ChunkSize = 1 << 20;

    /// <summary>
    /// Decrypts and unpacks a PS Vita .pkg as a NoNpDrm folder, laid out as on a memory card:
    /// <c>app/TITLEID</c>, <c>addcont/TITLEID/LABEL</c>, <c>patch/TITLEID</c> or <c>psm/TITLEID</c>
    /// under <paramref name="outputFolder"/>. Same output as pkg2zip -x. The folder is built under a
    /// temporary name and renamed when complete.
    /// </summary>
    /// <param name="zrif">The license. Games, DLC and PSM titles need one to run; updates don't, and it is ignored for them.</param>
    /// <exception cref="VitaFormatException">Not a Vita package, or damaged.</exception>
    /// <exception cref="MissingKeyException">The package's key isn't loaded.</exception>
    /// <exception cref="MissingLicenseException">The zRIF is for another package.</exception>
    /// <exception cref="IOException">The output exists and <see cref="VitaConvertOptions.Overwrite"/> is off.</exception>
    public async Task<VitaOperationResult> ExtractPkgAsync(string pkgPath, string? zrif, string outputFolder,
        VitaConvertOptions? options = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var pkg = PkgFile.Open(pkgPath);
        if (!pkg.IsVita) throw new VitaFormatException(pkg.Kind == PkgContentKind.Unknown
            ? $"Unsupported package content type 0x{pkg.ContentType:X}."
            : $"{pkg.Kind} packages aren't supported, only PS Vita games, DLC, updates and PSM titles.");
        var warnings = new List<string>();
        var rif = License(pkg, zrif, warnings);
        var key = keys.ContentKey(pkg.Header);
        var items = pkg.ReadItems(key);

        var root = Path.GetFullPath(Path.Combine(outputFolder, RootFor(pkg)));
        if (Directory.Exists(root) && !options.Overwrite) throw new IOException($"{root} already exists.");
        var temp = root + TempSuffix;
        if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        Directory.CreateDirectory(temp);
        try
        {
            await WriteItemsAsync(pkg, key, items, temp, progress, cancellationToken);
            await WriteExtrasAsync(pkg, rif, temp, cancellationToken);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            Directory.Move(temp, root);
        }
        catch
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        progress?.Report(new("Done", 1));
        return new VitaOperationResult(root, VitaFileType.NoNpDrmFolder, warnings)
        {
            Kind = pkg.Kind, ContentId = pkg.ContentId, Licensed = rif is not null,
        };
    }

    /// <summary>The decoded zRIF, checked against the package; null when there is none or it isn't needed.</summary>
    private static byte[]? License(PkgFile pkg, string? zrif, List<string> warnings)
    {
        if (pkg.Kind == PkgContentKind.VitaPatch)
        {
            if (zrif is not null) warnings.Add("Updates need no license; the zRIF was ignored.");
            return null;
        }
        if (zrif is null)
        {
            warnings.Add(pkg.Kind == PkgContentKind.Psm
                ? "No zRIF: RO/License/FAKE.rif wasn't written, so the title won't run until a license is added."
                : "No zRIF: sce_sys/package/work.bin wasn't written, so the game won't run until a license is added.");
            return null;
        }
        var rif = Zrif.Decode(zrif);
        var expected = pkg.Kind == PkgContentKind.Psm ? Zrif.PsmRifSize : Zrif.VitaRifSize;
        if (rif.Length != expected)
            throw new MissingLicenseException($"The zRIF is a {(rif.Length == Zrif.PsmRifSize ? "PSM" : "Vita")} license, but this is a {(expected == Zrif.PsmRifSize ? "PSM" : "Vita")} package.", pkg.ContentId);
        var licensed = Zrif.ContentIdOf(rif);
        if (licensed != pkg.ContentId)
            throw new MissingLicenseException($"The zRIF is for {licensed}, but the package is {pkg.ContentId}.", pkg.ContentId);
        return rif;
    }

    private static string RootFor(PkgFile pkg)
    {
        var id = pkg.TitleId ?? throw new VitaFormatException($".pkg content ID '{pkg.ContentId}' isn't valid.");
        return pkg.Kind switch
        {
            PkgContentKind.VitaApp => Path.Combine("app", id),
            PkgContentKind.VitaDlc => Path.Combine("addcont", id, pkg.ContentId[20..]),
            PkgContentKind.VitaPatch => Path.Combine("patch", id),
            _ => Path.Combine("psm", id),
        };
    }

    private static async Task WriteItemsAsync(PkgFile pkg, byte[] key, IReadOnlyList<PkgItem> items, string root,
        IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        using var aes = AesCtr.Create(key);
        await using var input = new FileStream(pkg.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous);
        var buffer = new byte[ChunkSize];
        var total = Math.Max(1.0, items.Where(i => !i.IsFolder).Sum(i => (double)i.DataSize));
        var done = 0.0;
        var psm = pkg.Kind == PkgContentKind.Psm;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var name = item.Name;
            if (psm)
            {
                // PSM packages keep everything under "content/"; the console reads it from RO/.
                if (!name.StartsWith("content/", StringComparison.Ordinal)) continue;
                name = "RO/" + name["content/".Length..];
            }
            var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            if (item.IsFolder)
            {
                Directory.CreateDirectory(path);
                continue;
            }

            // digs.bin is kept as it is, under the name NoNpDrm expects.
            var decrypt = true;
            if (!psm && name == "sce_sys/package/digs.bin")
            {
                path = Path.Combine(root, "sce_sys", "package", "body.bin");
                decrypt = false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous);
            var offset = item.DataOffset;
            var left = item.DataSize;
            while (left > 0)
            {
                ct.ThrowIfCancellationRequested();
                var size = (int)Math.Min(left, (ulong)buffer.Length);
                input.Position = (long)(pkg.Header.DataOffset + offset);
                await input.ReadExactlyAsync(buffer.AsMemory(0, size), ct);
                if (decrypt) AesCtr.Xor(aes, pkg.Header.Iv, offset / 16, buffer.AsSpan(0, size));
                await output.WriteAsync(buffer.AsMemory(0, size), ct);
                offset += (ulong)size;
                left -= (ulong)size;
                done += size;
                progress?.Report(new("Unpacking", done / total));
            }
        }
    }

    /// <summary>The files NoNpDrm and the console expect beside the package's own, as pkg2zip writes them.</summary>
    private static async Task WriteExtrasAsync(PkgFile pkg, byte[]? rif, string root, CancellationToken ct)
    {
        if (pkg.Kind == PkgContentKind.Psm)
        {
            if (rif is not null)
            {
                Directory.CreateDirectory(Path.Combine(root, "RO", "License"));
                await File.WriteAllBytesAsync(Path.Combine(root, "RO", "License", "FAKE.rif"), rif, ct);
            }
            foreach (var folder in new[] { "Documents", "Temp", "System" })
                Directory.CreateDirectory(Path.Combine(root, "RW", folder));
            var contentId = new byte[0x30];
            Encoding.ASCII.GetBytes(pkg.Header.ContentId).CopyTo(contentId, 0);
            await File.WriteAllBytesAsync(Path.Combine(root, "RW", "System", "content_id"), contentId, ct);
            await File.WriteAllBytesAsync(Path.Combine(root, "RW", "System", "pm.dat"), new byte[0x10000], ct);
            return;
        }

        var package = Path.Combine(root, "sce_sys", "package");
        Directory.CreateDirectory(package);
        await using (var input = new FileStream(pkg.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous))
        {
            // head.bin: everything up to the end of the item table; tail.bin: everything after the encrypted data.
            var headSize = (long)pkg.Header.DataOffset + pkg.ItemsSize;
            await CopyRangeAsync(input, 0, headSize, Path.Combine(package, "head.bin"), ct);
            var tailStart = (long)(pkg.Header.DataOffset + pkg.Header.DataSize);
            await CopyRangeAsync(input, tailStart, Math.Max(0, pkg.Length - tailStart), Path.Combine(package, "tail.bin"), ct);
        }
        await File.WriteAllBytesAsync(Path.Combine(package, "stat.bin"), new byte[768], ct);
        if (rif is not null) await File.WriteAllBytesAsync(Path.Combine(package, "work.bin"), rif, ct);
    }

    private static async Task CopyRangeAsync(FileStream input, long start, long length, string path, CancellationToken ct)
    {
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous);
        var buffer = new byte[Math.Min(ChunkSize, Math.Max(1, length))];
        input.Position = start;
        while (length > 0)
        {
            var size = (int)Math.Min(length, buffer.Length);
            await input.ReadExactlyAsync(buffer.AsMemory(0, size), ct);
            await output.WriteAsync(buffer.AsMemory(0, size), ct);
            length -= size;
        }
    }
}
