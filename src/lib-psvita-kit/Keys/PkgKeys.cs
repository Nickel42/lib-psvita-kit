using LibPsVitaKit.Crypto;
using LibPsVitaKit.Pkg;

namespace LibPsVitaKit.Keys;

/// <summary>
/// The PS Vita package keys. A package's header names which one it uses (key type 2, 3 or 4); its
/// content key is that key's AES-ECB encryption of the package IV. <see cref="BuiltIn"/> has all
/// three; a caller can still supply or override them.
/// </summary>
public sealed class PkgKeys
{
    // The public PS Vita package keys, as published on the PS Vita developer wiki and in pkg2zip.
    private static readonly byte[] VitaKey2 = [0xE3, 0x1A, 0x70, 0xC9, 0xCE, 0x1D, 0xD7, 0x2B, 0xF3, 0xC0, 0x62, 0x29, 0x63, 0xF2, 0xEC, 0xCB];
    private static readonly byte[] VitaKey3 = [0x42, 0x3A, 0xCA, 0x3A, 0x2B, 0xD5, 0x64, 0x9F, 0x96, 0x86, 0xAB, 0xAD, 0x6F, 0xD8, 0x80, 0x1F];
    private static readonly byte[] VitaKey4 = [0xAF, 0x07, 0xFD, 0x59, 0x65, 0x25, 0x27, 0xBA, 0xF1, 0x33, 0x89, 0x66, 0x8B, 0x17, 0xD9, 0xEA];

    /// <summary>A new set holding the built-in keys for key types 2, 3 and 4. Later <see cref="Set"/> calls override them.</summary>
    public static PkgKeys BuiltIn() => new PkgKeys().Set(2, VitaKey2).Set(3, VitaKey3).Set(4, VitaKey4);

    /// <summary>The built-in keys, overridden by any found in a key file (see <see cref="Parse"/>).</summary>
    public static PkgKeys BuiltInWith(string keyFilePath)
    {
        var keys = BuiltIn();
        foreach (var (type, key) in Load(keyFilePath)._keys) keys.Set(type, key);
        return keys;
    }

    /// <summary>The names the key file uses, after the PS Vita developer wiki and pkg2zip.</summary>
    public static readonly IReadOnlyDictionary<int, string> KeyNames = new Dictionary<int, string>
    {
        [2] = "pkg_vita_2",
        [3] = "pkg_vita_3",
        [4] = "pkg_vita_4",
    };

    private readonly Dictionary<int, byte[]> _keys = [];

    /// <summary>Sets the key for one key type (2, 3 or 4).</summary>
    /// <exception cref="ArgumentException">Not a 16-byte key, or not key type 2, 3 or 4.</exception>
    public PkgKeys Set(int keyType, ReadOnlySpan<byte> key)
    {
        if (!KeyNames.ContainsKey(keyType)) throw new ArgumentException($"Key type {keyType} isn't a PS Vita package key (2, 3 or 4).", nameof(keyType));
        if (key.Length != 16) throw new ArgumentException("A package key is 16 bytes.", nameof(key));
        _keys[keyType] = key.ToArray();
        return this;
    }

    public bool Has(int keyType) => _keys.ContainsKey(keyType);

    /// <summary>
    /// Reads a key file: one <c>name = hex</c> (or <c>name: hex</c>) per line, names as in
    /// <see cref="KeyNames"/>. Blank lines and lines starting with # or ; are skipped, as are unknown names.
    /// </summary>
    /// <exception cref="FormatException">A known key whose value isn't 32 hex digits.</exception>
    public static PkgKeys Parse(string text)
    {
        var keys = new PkgKeys();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            var split = line.IndexOfAny(['=', ':']);
            if (split < 0) continue;
            var name = line[..split].Trim();
            var type = KeyNames.FirstOrDefault(k => k.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
            if (type == 0) continue;
            var hex = line[(split + 1)..].Trim().Replace(" ", "");
            byte[] key;
            try
            {
                key = Convert.FromHexString(hex);
            }
            catch (FormatException)
            {
                throw new FormatException($"{name} is not hex.");
            }
            if (key.Length != 16) throw new FormatException($"{name} should be 32 hex digits (16 bytes), not {hex.Length}.");
            keys.Set(type, key);
        }
        return keys;
    }

    public static PkgKeys Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>The key a package's contents are encrypted with.</summary>
    /// <exception cref="MissingKeyException">The package's key type wasn't loaded.</exception>
    /// <exception cref="VitaFormatException">Not a PS Vita key type (a PSP or PS3 package).</exception>
    public byte[] ContentKey(PkgHeader header)
    {
        if (!KeyNames.TryGetValue(header.KeyType, out var name))
            throw new VitaFormatException($"Key type {header.KeyType} isn't a PS Vita package (PSP and PS3 packages aren't supported).");
        if (!_keys.TryGetValue(header.KeyType, out var key))
            throw new MissingKeyException($"The package needs {name}, which isn't loaded.", name);
        return AesCtr.EncryptBlock(key, header.Iv);
    }
}
