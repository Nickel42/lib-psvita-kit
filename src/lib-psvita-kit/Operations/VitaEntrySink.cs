namespace LibPsVitaKit.Operations;

/// <summary>
/// Where an extraction puts its files and folders, for writing them somewhere other than a folder
/// (a zip, say) without a round trip through the disk. Entries come in package order, then the
/// files pkg2zip adds (head.bin, tail.bin, stat.bin, work.bin; for PSM, RO/License and RW/).
/// </summary>
public interface IVitaEntrySink
{
    /// <summary>A folder, '/'-separated and relative to the game's folder.</summary>
    void CreateFolder(string path);

    /// <summary>
    /// A file, '/'-separated and relative to the game's folder, that will get exactly
    /// <paramref name="length"/> bytes. The extraction writes them and disposes the stream. Its
    /// parent folders may not have been passed to <see cref="CreateFolder"/>. A damaged or unusual
    /// package can name a file twice; the later one should replace the earlier, as in a folder.
    /// </summary>
    Stream CreateFile(string path, long length);
}

/// <summary>Writes the entries under a folder, each file allocated at its full size up front.</summary>
internal sealed class FolderSink(string root) : IVitaEntrySink
{
    private readonly HashSet<string> _folders = new(StringComparer.Ordinal);

    public void CreateFolder(string path) => Ensure(Full(path));

    /// <summary>A new file; it is an error if the package already wrote one by that name.</summary>
    public Stream CreateFile(string path, long length) => Open(path, length, FileMode.CreateNew);

    /// <summary>A file that replaces any the package wrote by that name, as the extras pkg2zip adds do.</summary>
    public Stream ReplaceFile(string path, long length) => Open(path, length, FileMode.Create);

    private FileStream Open(string path, long length, FileMode mode)
    {
        var full = Full(path);
        Ensure(Path.GetDirectoryName(full)!);
        return new FileStream(full, new FileStreamOptions
        {
            Mode = mode, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 0,
            Options = FileOptions.Asynchronous, PreallocationSize = length,
        });
    }

    private string Full(string path) => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

    private void Ensure(string folder)
    {
        if (_folders.Add(folder)) Directory.CreateDirectory(folder);
    }
}
