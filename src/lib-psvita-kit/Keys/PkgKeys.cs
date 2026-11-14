using LibPsVitaKit.Crypto;
using LibPsVitaKit.Pkg;

namespace LibPsVitaKit.Keys;

/// <summary>
/// The PS Vita package keys, which the kit doesn't ship. A package's header names which one it
/// uses (key type 2, 3 or 4); its content key is that key's AES-ECB encryption of the package IV.
/// </summary>
public sealed class PkgKeys
{
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
