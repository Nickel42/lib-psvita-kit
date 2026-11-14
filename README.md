# lib-psvita-kit

A pure C# library for inspecting and converting PlayStation Vita files in any .NET app. It reads and writes `param.sfo`, recognises `.pkg`, `.vpk`, NoNpDrm game folders and the zips Vita3K installs, reads title and content IDs, and decodes zRIF licenses to `work.bin`. Unpacking a `.pkg` with its zRIF license into a NoNpDrm folder is next. No native code, no keys or licenses bundled, no dependencies.

> **Status: early.** Inspection, `param.sfo` and zRIF decoding work. zRIF encoding and the conversions are stubs that throw `NotImplementedException`. See [docs/SPEC.md](docs/SPEC.md) for the plan.

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
| `.pkg` (PlayStation Store package) | ✓ | content ID, title ID (header) | → NoNpDrm folder: planned |
| NoNpDrm folder (`sce_sys/param.sfo`) | ✓ | from `param.sfo` | → `.vpk` / zip: planned |
| Zip of a NoNpDrm folder (`TITLEID/…` or `app/TITLEID/…`) | ✓ | from `param.sfo` | → folder: planned |
| `.vpk` (homebrew installer) | ✓ | from `param.sfo` | → folder: planned |
| `param.sfo` | ✓ | every key | read and write ✓ |
| zRIF license string | — | content ID | → RIF (`work.bin`, or PSM `FAKE.rif`) ✓; RIF → zRIF: planned |

## Use

```csharp
using LibPsVitaKit;
using LibPsVitaKit.Licenses;
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
```

Errors: `VitaFormatException` for a file or zRIF that isn't valid, and `MissingLicenseException` (once conversions land) for a missing or wrong zRIF.

## Command line

```text
psvita-kit info <file-or-folder>   What it is, and its title, title ID and content ID
psvita-kit sfo <param.sfo>         Every key in a param.sfo
psvita-kit zrif <zRIF> [work.bin]  The content ID a zRIF is for; with a path, write its RIF there
```

## Build and test

```bash
dotnet build lib-psvita-kit.sln -c Release
dotnet test lib-psvita-kit.sln -c Release
```

The tests run on both `net8.0` and `net10.0`, so the .NET 8 runtime is needed for the first.

## License

MIT. See [LICENSE](LICENSE), and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the zRIF dictionary from pkg2zip.
