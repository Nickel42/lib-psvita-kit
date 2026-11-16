# lib-psvita-kit

A pure C# library for inspecting and converting PlayStation Vita files in any .NET app. It reads and writes `param.sfo`, recognises `.pkg`, `.vpk`, NoNpDrm game folders and the zips Vita3K installs, reads title and content IDs, decodes zRIF licenses, and unpacks PS Vita `.pkg` files (games, DLC, updates and PSM titles) into NoNpDrm folders, the same output as `pkg2zip -x`. No native code, no dependencies. The PS Vita package keys are built in; licenses (zRIFs) are not.

> **Status: early.** Tested on synthetic packages only so far. zRIF encoding and folder ↔ zip/`.vpk` are still to come. See [docs/SPEC.md](docs/SPEC.md) for the plan.

| Package | What it is |
|---|---|
| **lib-psvita-kit** | The library (namespace `LibPsVitaKit`) |
| **lib-psvita-kit.Cli** | The `psvita-kit` command-line tool, as a `dotnet tool` |

## Platforms

Plain managed code targeting `net8.0` and `net10.0`, marked trim- and AOT-compatible. Any app on .NET 8 or later can use it, on Windows, Linux, macOS, Android or iOS.

## Install

Until a release reaches nuget.org, add the repository as a git submodule and reference `src/lib-psvita-kit/lib-psvita-kit.csproj`.

```bash
dotnet add package lib-psvita-kit
dotnet tool install -g lib-psvita-kit.Cli
```

## What it handles

| Input | Recognise | Read IDs | Convert |
|---|---|---|---|
| `.pkg` (PlayStation Store package) | ✓ | title, content ID, title ID, kind (no keys needed) | → NoNpDrm folder ✓ (Vita game, DLC, update, PSM; not PSP or PS1) |
| NoNpDrm folder (`sce_sys/param.sfo`) | ✓ | from `param.sfo` | → `.vpk` / zip: planned |
| Zip of a NoNpDrm folder (`TITLEID/…` or `app/TITLEID/…`) | ✓ | from `param.sfo` | → folder: planned |
| `.vpk` (homebrew installer) | ✓ | from `param.sfo` | → folder: planned |
| `param.sfo` | ✓ | every key | read and write ✓ |
| zRIF license string | — | content ID | → RIF (`work.bin`, or PSM `FAKE.rif`) ✓; RIF → zRIF: planned |

## Use

```csharp
using LibPsVitaKit;
using LibPsVitaKit.Keys;
using LibPsVitaKit.Licenses;
using LibPsVitaKit.Operations;
using LibPsVitaKit.Sfo;

// What is it? Reads headers only.
VitaFileInfo info = VitaFile.Inspect(path);
Console.WriteLine($"{info.Type}: {info.Title} [{info.TitleId}] {info.ContentId}");

// param.sfo: read, change, write.
var sfo = SfoFile.Load("sce_sys/param.sfo");
sfo.SetText("TITLE", "My Game", maxLength: 0x80);
sfo.Save("param.sfo");

// zRIF → work.bin (512 bytes for a Vita title, 1024 for PSM).
byte[] rif = Zrif.Decode(zrif);                   // VitaFormatException if it's corrupt
if (Zrif.ContentIdOf(rif) != info.ContentId) { /* the license is for another package */ }
File.WriteAllBytes("sce_sys/package/work.bin", rif);

// .pkg + zRIF → NoNpDrm folder: <output>/app/PCSE00000 (or addcont/…, patch/…, psm/…).
var converter = new VitaConverter(); // built-in package keys
VitaOperationResult result = await converter.ExtractPkgAsync("game.pkg", zrif, outputFolder,
    new VitaConvertOptions { Overwrite = false }, progress);
Console.WriteLine($"{result.Kind} → {result.OutputPath}, licensed: {result.Licensed}");
foreach (var w in result.Warnings) Console.WriteLine(w); // e.g. no zRIF given, so no work.bin

// Or straight into something else, such as a zip, with no folder on disk: implement IVitaEntrySink
// (CreateFolder, CreateFile(path, length) → Stream). Paths are relative to the game's folder.
var pkg = PkgFile.Open("game.pkg");                // an opened package is reused, not read again
result = await converter.ExtractPkgAsync(pkg, zrif, mySink, progress); // OutputPath: "app/PCSE00000"
```

Errors: `VitaFormatException` for a file or zRIF that isn't valid, `MissingKeyException` when the package's key isn't loaded, and `MissingLicenseException` when the zRIF is for another package. The folder is built under a temporary name and only renamed into place once it's complete, so a failure or cancellation leaves nothing half-written.

## Keys

Packages are encrypted with one of three PS Vita package keys (key type 2, 3 or 4 in the header). All three are built in: `new VitaConverter()` uses them (`PkgKeys.BuiltIn()`). To use other keys, pass your own `PkgKeys` (`Set`, `Load`), or start from the built-in ones and override some with a text file (`PkgKeys.BuiltInWith(path)`):

```text
pkg_vita_2 = <32 hex digits>
pkg_vita_3 = <32 hex digits>
pkg_vita_4 = <32 hex digits>
```

Reading a package's title, IDs and kind (`VitaFile.Inspect`, `PkgFile.Open`) needs no keys.

## Command line

```text
psvita-kit info <file-or-folder>   What it is, and its title, title ID and content ID
psvita-kit sfo <param.sfo>         Every key in a param.sfo
psvita-kit zrif <zRIF> [work.bin]  The content ID a zRIF is for; with a path, write its RIF there
psvita-kit extract [--keys <file>] [--overwrite] <game.pkg> <output-folder> [zRIF]
                                   Unpack a Vita .pkg as a NoNpDrm folder
```

## Build and test

```bash
dotnet build lib-psvita-kit.sln -c Release
dotnet test lib-psvita-kit.sln -c Release
```

The tests run on both `net8.0` and `net10.0`, so the .NET 8 runtime is needed for the first.

## License

MIT. See [LICENSE](LICENSE), and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the zRIF dictionary from pkg2zip.
