using System.Text;
using LibPsVitaKit.Crypto;
using LibPsVitaKit.Keys;
using LibPsVitaKit.Licenses;
using LibPsVitaKit.Pkg;
using Microsoft.Win32.SafeHandles;

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
/// updates and PSM titles, written to disk or to an <see cref="IVitaEntrySink"/>. Planned (docs/SPEC.md): NoNpDrm folder ↔ .vpk and zip.
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
        => await ExtractPkgAsync(PkgFile.Open(pkgPath), zrif, outputFolder, options, progress, cancellationToken);

    /// <summary>
    /// As <see cref="ExtractPkgAsync(string, string?, string, VitaConvertOptions?, IProgress{OperationProgress}?, CancellationToken)"/>,
    /// for a package the caller has already opened, so its header isn't read again.
    /// </summary>
    public async Task<VitaOperationResult> ExtractPkgAsync(PkgFile pkg, string? zrif, string outputFolder,
        VitaConvertOptions? options = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pkg);
        options ??= new();
        var (rif, key, items, warnings) = Prepare(pkg, zrif);

        var root = Path.GetFullPath(Path.Combine(outputFolder, RootFor(pkg)));
        if (Directory.Exists(root) && !options.Overwrite) throw new IOException($"{root} already exists.");
        var temp = root + TempSuffix;
        if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        Directory.CreateDirectory(temp);
        try
        {
            var sink = new FolderSink(temp);
            await WriteItemsAsync(pkg, key, items, sink, progress, cancellationToken);
            await WriteExtrasAsync(pkg, rif, sink, cancellationToken);
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

    /// <summary>
    /// Decrypts and unpacks a PS Vita .pkg into <paramref name="sink"/> instead of a folder: the same
    /// files, with the same bytes, as the folder overloads write, named relative to the game's folder.
    /// <see cref="VitaOperationResult.OutputPath"/> is that folder's place on a memory card, such as
    /// <c>app/PCSE00000</c>. If this throws, the sink may hold some of the entries; the caller discards them.
    /// </summary>
    /// <param name="zrif">The license. Games, DLC and PSM titles need one to run; updates don't, and it is ignored for them.</param>
    /// <exception cref="VitaFormatException">Not a Vita package, or damaged.</exception>
    /// <exception cref="MissingKeyException">The package's key isn't loaded.</exception>
    /// <exception cref="MissingLicenseException">The zRIF is for another package.</exception>
    public async Task<VitaOperationResult> ExtractPkgAsync(PkgFile pkg, string? zrif, IVitaEntrySink sink,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pkg);
        ArgumentNullException.ThrowIfNull(sink);
        var (rif, key, items, warnings) = Prepare(pkg, zrif);
        var root = RootFor(pkg).Replace(Path.DirectorySeparatorChar, '/');
        await WriteItemsAsync(pkg, key, items, sink, progress, cancellationToken);
        await WriteExtrasAsync(pkg, rif, sink, cancellationToken);
        progress?.Report(new("Done", 1));
        return new VitaOperationResult(root, VitaFileType.NoNpDrmFolder, warnings)
        {
            Kind = pkg.Kind, ContentId = pkg.ContentId, Licensed = rif is not null,
        };
    }

    /// <summary>Checks the package and license and decrypts the item table: everything before the first file is written.</summary>
    private (byte[]? Rif, byte[] Key, IReadOnlyList<PkgItem> Items, List<string> Warnings) Prepare(PkgFile pkg, string? zrif)
    {
        if (!pkg.IsVita) throw new VitaFormatException(pkg.Kind == PkgContentKind.Unknown
            ? $"Unsupported package content type 0x{pkg.ContentType:X}."
            : $"{pkg.Kind} packages aren't supported, only PS Vita games, DLC, updates and PSM titles.");
        var warnings = new List<string>();
        var rif = License(pkg, zrif, warnings);
        var key = keys.ContentKey(pkg.Header);
        return (rif, key, pkg.ReadItems(key), warnings);
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

    private static async Task WriteItemsAsync(PkgFile pkg, byte[] key, IReadOnlyList<PkgItem> items, IVitaEntrySink sink,
        IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        using var aes = AesCtr.Create(key);
        using var input = File.OpenHandle(pkg.Path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous | FileOptions.SequentialScan);
        // Two buffers: the next chunk is read while this one is decrypted and written.
        byte[][] buffers = [new byte[ChunkSize], new byte[ChunkSize]];
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
            if (item.IsFolder)
            {
                sink.CreateFolder(name);
                continue;
            }

            // digs.bin is kept as it is, under the name NoNpDrm expects.
            var decrypt = true;
            if (!psm && name == "sce_sys/package/digs.bin")
            {
                name = "sce_sys/package/body.bin";
                decrypt = false;
            }
            var length = (long)item.DataSize;
            var start = (long)(pkg.Header.DataOffset + item.DataOffset);
            await using var output = sink.CreateFile(name, length);
            var reading = length > 0 ? ReadExactlyAsync(input, buffers[0].AsMemory(0, (int)Math.Min(length, ChunkSize)), start, ct) : null;
            try
            {
                for (long at = 0, current = 0; at < length; current ^= 1)
                {
                    await reading!;
                    var size = (int)Math.Min(length - at, ChunkSize);
                    var next = at + size;
                    reading = next < length
                        ? ReadExactlyAsync(input, buffers[current ^ 1].AsMemory(0, (int)Math.Min(length - next, ChunkSize)), start + next, ct)
                        : null;
                    var chunk = buffers[current].AsMemory(0, size);
                    if (decrypt) AesCtr.Xor(aes, pkg.Header.Iv, (item.DataOffset + (ulong)at) / 16, chunk.Span);
                    await output.WriteAsync(chunk, ct);
                    at = next;
                    done += size;
                    progress?.Report(new("Unpacking", done / total));
                }
            }
            finally
            {
                // Don't leave a read running into a buffer, or on the handle, after a failure.
                if (reading is not null)
                {
                    try { await reading; } catch { }
                }
            }
        }
    }

    private static async Task ReadExactlyAsync(SafeFileHandle input, Memory<byte> buffer, long offset, CancellationToken ct)
    {
        while (!buffer.IsEmpty)
        {
            var read = await RandomAccess.ReadAsync(input, buffer, offset, ct);
            if (read == 0) throw new EndOfStreamException();
            buffer = buffer[read..];
            offset += read;
        }
    }

    /// <summary>The files NoNpDrm and the console expect beside the package's own, as pkg2zip writes them.</summary>
    private static async Task WriteExtrasAsync(PkgFile pkg, byte[]? rif, IVitaEntrySink sink, CancellationToken ct)
    {
        if (pkg.Kind == PkgContentKind.Psm)
        {
            if (rif is not null) await WriteAsync(sink, "RO/License/FAKE.rif", rif, ct);
            foreach (var folder in new[] { "Documents", "Temp", "System" }) sink.CreateFolder("RW/" + folder);
            var contentId = new byte[0x30];
            Encoding.ASCII.GetBytes(pkg.Header.ContentId).CopyTo(contentId, 0);
            await WriteAsync(sink, "RW/System/content_id", contentId, ct);
            await WriteAsync(sink, "RW/System/pm.dat", new byte[0x10000], ct);
            return;
        }

        sink.CreateFolder("sce_sys/package");
        using (var input = File.OpenHandle(pkg.Path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous))
        {
            // head.bin: everything up to the end of the item table; tail.bin: everything after the encrypted data.
            var headSize = (long)pkg.Header.DataOffset + pkg.ItemsSize;
            await CopyRangeAsync(input, 0, headSize, sink, "sce_sys/package/head.bin", ct);
            var tailStart = (long)(pkg.Header.DataOffset + pkg.Header.DataSize);
            await CopyRangeAsync(input, tailStart, Math.Max(0, pkg.Length - tailStart), sink, "sce_sys/package/tail.bin", ct);
        }
        await WriteAsync(sink, "sce_sys/package/stat.bin", new byte[768], ct);
        if (rif is not null) await WriteAsync(sink, "sce_sys/package/work.bin", rif, ct);
    }

    /// <summary>In a folder, extras replace a package file of the same name, as pkg2zip's do.</summary>
    private static Stream CreateExtra(IVitaEntrySink sink, string path, long length)
        => sink is FolderSink folder ? folder.ReplaceFile(path, length) : sink.CreateFile(path, length);

    private static async Task WriteAsync(IVitaEntrySink sink, string path, byte[] data, CancellationToken ct)
    {
        await using var output = CreateExtra(sink, path, data.Length);
        await output.WriteAsync(data, ct);
    }

    private static async Task CopyRangeAsync(SafeFileHandle input, long start, long length, IVitaEntrySink sink, string path, CancellationToken ct)
    {
        await using var output = CreateExtra(sink, path, length);
        var buffer = new byte[Math.Min(ChunkSize, Math.Max(1, length))];
        while (length > 0)
        {
            var size = (int)Math.Min(length, buffer.Length);
            await ReadExactlyAsync(input, buffer.AsMemory(0, size), start, ct);
            await output.WriteAsync(buffer.AsMemory(0, size), ct);
            start += size;
            length -= size;
        }
    }
}
