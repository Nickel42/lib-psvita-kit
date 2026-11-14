namespace LibPsVitaKit;

/// <summary>The file is not a valid or supported PS Vita file.</summary>
public class VitaFormatException : Exception
{
    public VitaFormatException(string message) : base(message) { }
}

/// <summary>A package key needed for the operation was not loaded.</summary>
public class MissingKeyException : Exception
{
    public MissingKeyException(string message, string keyName) : base(message) => KeyName = keyName;

    /// <summary>The key's name, e.g. "pkg_vita_2".</summary>
    public string KeyName { get; }
}

/// <summary>A license (zRIF or work.bin) needed to decrypt the content was not given or does not match.</summary>
public class MissingLicenseException : Exception
{
    public MissingLicenseException(string message, string contentId) : base(message) => ContentId = contentId;

    /// <summary>The content ID the license is for, e.g. "UP0000-PCSE00000_00-0000000000000000".</summary>
    public string ContentId { get; }
}
