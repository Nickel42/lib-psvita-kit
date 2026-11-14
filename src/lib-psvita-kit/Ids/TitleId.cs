namespace LibPsVitaKit.Ids;

/// <summary>A title ID such as <c>PCSE00000</c>: four uppercase letters and five digits.</summary>
public readonly record struct TitleId
{
    public string Value { get; }

    private TitleId(string value) => Value = value;

    public static bool IsValid(ReadOnlySpan<char> s) =>
        s.Length == 9 && s[..4].IndexOfAnyExceptInRange('A', 'Z') < 0 && s[4..].IndexOfAnyExceptInRange('0', '9') < 0;

    public static bool TryParse(string? s, out TitleId id)
    {
        id = default;
        if (s is null || !IsValid(s)) return false;
        id = new TitleId(s);
        return true;
    }

    /// <exception cref="FormatException">Not a title ID.</exception>
    public static TitleId Parse(string s) => TryParse(s, out var id) ? id : throw new FormatException($"'{s}' is not a title ID (e.g. PCSE00000).");

    public override string ToString() => Value ?? "";
}
