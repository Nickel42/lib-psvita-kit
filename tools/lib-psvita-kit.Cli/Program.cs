// psvita-kit: inspect PS Vita files. Conversions are added here as the library gains them.
using LibPsVitaKit;
using LibPsVitaKit.Licenses;
using LibPsVitaKit.Sfo;

const string Usage = """
    usage: psvita-kit <command> [arguments]

      info <file-or-folder>   What a .pkg, .vpk, NoNpDrm folder or zip, or param.sfo is, and its IDs
      sfo <param.sfo>         Every key in a param.sfo
      zrif <zRIF> [work.bin]  The content ID a zRIF is for; with a path, write its RIF there
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
            Console.WriteLine($"Type:        {info.Type}");
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
        default:
            Console.Error.WriteLine(Usage);
            return 1;
    }
}
catch (Exception ex) when (ex is VitaFormatException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 3;
}
