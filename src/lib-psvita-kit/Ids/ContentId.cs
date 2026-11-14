namespace LibPsVitaKit.Ids;

/// <summary>
/// A content ID such as <c>UP0000-PCSE00000_00-0000000000000000</c>: service and publisher
/// (<c>UP0000</c>), title ID, <c>_00</c>, and a 16-character label. 36 characters.
/// </summary>
public readonly record struct ContentId
{
    public const int Length = 36;

    public string Value { get; }
    /// <summary>"UP0000": service prefix (UP = US, EP = Europe, JP, HP, ...) and publisher number.</summary>
    public string Publisher => Value[..6];
    public TitleId TitleId => TitleId.Parse(Value[7..16]);
    public string Label => Value[20..];

    private ContentId(string value) => Value = value;

    public static bool TryParse(string? s, out ContentId id)
    {
        id = default;
        if (s is null) return false;
        s = s.TrimEnd('\0');
        if (s.Length != Length || s[6] != '-' || s[16] != '_' || s[19] != '-' || !TitleId.IsValid(s.AsSpan(7, 9))) return false;
        id = new ContentId(s);
        return true;
    }

    /// <exception cref="FormatException">Not a content ID.</exception>
    public static ContentId Parse(string s) => TryParse(s, out var id) ? id : throw new FormatException($"'{s}' is not a content ID.");

    public override string ToString() => Value ?? "";
}
