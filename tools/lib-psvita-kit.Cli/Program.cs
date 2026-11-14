// psvita-kit: inspect and convert PS Vita files.
using LibPsVitaKit;
using LibPsVitaKit.Keys;
using LibPsVitaKit.Licenses;
using LibPsVitaKit.Operations;
using LibPsVitaKit.Sfo;

const string Usage = """
    usage: psvita-kit <command> [arguments]

      info <file-or-folder>   What a .pkg, .vpk, NoNpDrm folder or zip, or param.sfo is, and its IDs
      sfo <param.sfo>         Every key in a param.sfo
      zrif <zRIF> [work.bin]  The content ID a zRIF is for; with a path, write its RIF there
      extract [--keys <file>] [--overwrite] <game.pkg> <output-folder> [zRIF]
                              Unpack a Vita .pkg as a NoNpDrm folder (app/, addcont/, patch/ or psm/
                              under the output folder). The package keys are built in; a key file
                              (pkg_vita_2/3/4 = <hex>) overrides them.
      --help                  This text
    """;

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 1 : 0;
}

try
{
    switch (args[0])
    {
        case "info" when args.Length == 2:
            var info = VitaFile.Inspect(args[1]);
            Console.WriteLine($"Type:        {info.Type}{(info.PkgKind is { } kind ? $" ({kind})" : "")}");
            if (info.Title is not null) Console.WriteLine($"Title:       {info.Title}");
            if (info.TitleId is not null) Console.WriteLine($"Title ID:    {info.TitleId}");
            if (info.ContentId is not null) Console.WriteLine($"Content ID:  {info.ContentId}");
            if (info.Category is not null) Console.WriteLine($"Category:    {info.Category}");
            if (info.AppVersion is not null) Console.WriteLine($"App version: {info.AppVersion}");
            return info.Type == VitaFileType.Unknown ? 2 : 0;
        case "sfo" when args.Length == 2:
            foreach (var (key, entry) in SfoFile.Load(args[1]).Entries.OrderBy(e => e.Key, StringComparer.Ordinal))
                Console.WriteLine($"{key,-24} {entry}");
            return 0;
        case "zrif" when args.Length is 2 or 3:
            var rif = Zrif.Decode(args[1]);
            Console.WriteLine($"Content ID:  {Zrif.ContentIdOf(rif)}");
            Console.WriteLine($"RIF:         {rif.Length} bytes ({(rif.Length == Zrif.PsmRifSize ? "PSM" : "Vita")})");
            if (args.Length == 3)
            {
                File.WriteAllBytes(args[2], rif);
                Console.WriteLine($"Wrote {args[2]}");
            }
            return 0;
        case "extract":
            return await Extract(args[1..]);
        default:
            Console.Error.WriteLine(Usage);
            return 1;
    }
}
catch (Exception ex) when (ex is VitaFormatException or MissingKeyException or MissingLicenseException
    or IOException or UnauthorizedAccessException or FormatException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 3;
}

async Task<int> Extract(string[] rest)
{
    string? keyFile = null;
    var overwrite = false;
    var positional = new List<string>();
    for (var i = 0; i < rest.Length; i++)
    {
        if (rest[i] == "--keys" && i + 1 < rest.Length) keyFile = rest[++i];
        else if (rest[i] == "--overwrite") overwrite = true;
        else positional.Add(rest[i]);
    }
    if (positional.Count is not (2 or 3))
    {
        Console.Error.WriteLine(Usage);
        return 1;
    }
    var converter = new VitaConverter(keyFile is null ? PkgKeys.BuiltIn() : PkgKeys.BuiltInWith(keyFile));
    var progress = new Progress<OperationProgress>(p => Console.Write($"\r{p.Stage}: {p.Fraction:P0}   "));
    var result = await converter.ExtractPkgAsync(positional[0], positional.Count == 3 ? positional[2] : null, positional[1],
        new VitaConvertOptions { Overwrite = overwrite }, progress);
    Console.WriteLine($"\rWrote {result.OutputPath} ({result.Kind}, {result.ContentId}{(result.Licensed ? ", licensed" : "")})");
    foreach (var w in result.Warnings) Console.WriteLine($"  note: {w}");
    return 0;
}
