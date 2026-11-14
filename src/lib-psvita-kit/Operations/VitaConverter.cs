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
public sealed record VitaOperationResult(string OutputPath, VitaFileType OutputType, IReadOnlyList<string> Warnings);

/// <summary>
/// The conversions between PS Vita formats. Planned (see docs/SPEC.md):
/// <list type="bullet">
/// <item>.pkg + zRIF → NoNpDrm folder (games, DLC, updates)</item>
/// <item>NoNpDrm folder ↔ .vpk</item>
/// <item>NoNpDrm folder → a zip Vita3K installs</item>
/// </list>
/// </summary>
public sealed class VitaConverter
{
    /// <summary>Decrypts and unpacks a .pkg into a NoNpDrm folder under <paramref name="outputFolder"/>.</summary>
    /// <param name="zrif">The package's license; null for content that needs none (some updates, free DLC).</param>
    /// <exception cref="NotImplementedException">Not implemented yet.</exception>
    public Task<VitaOperationResult> ExtractPkgAsync(string pkgPath, string? zrif, string outputFolder,
        VitaConvertOptions? options = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(".pkg extraction is not implemented yet.");
}
