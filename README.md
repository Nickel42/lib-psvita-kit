# lib-psvita-kit

A pure C# library for inspecting and converting PlayStation Vita files in any .NET app. It reads and writes `param.sfo`, recognises `.pkg`, `.vpk`, NoNpDrm game folders and the zips Vita3K installs, and reads title and content IDs. Unpacking a `.pkg` with its zRIF license into a NoNpDrm folder is next. No native code, no keys or licenses bundled, no dependencies.

> **Status: early.** Inspection and `param.sfo` work. Conversions are stubs that throw `NotImplementedException`. See [docs/SPEC.md](docs/SPEC.md) for the plan.

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

## Use

```csharp
using LibPsVitaKit;
using LibPsVitaKit.Sfo;

// What is it? Reads headers only.
VitaFileInfo info = VitaFile.Inspect(path);
Console.WriteLine($"{info.Type}: {info.Title} [{info.TitleId}] {info.ContentId}");

// param.sfo: read, change, write.
var sfo = SfoFile.Load("sce_sys/param.sfo");
sfo.SetText("TITLE", "My Game", maxLength: 0x80);
sfo.Save("param.sfo");
```

Errors: `VitaFormatException` for a file that isn't valid, and `MissingLicenseException` (once conversions land) for a missing or wrong zRIF.

## Command line

```text
psvita-kit info <file-or-folder>   What it is, and its title, title ID and content ID
psvita-kit sfo <param.sfo>         Every key in a param.sfo
```

## Build and test

```bash
dotnet build lib-psvita-kit.sln -c Release
dotnet test lib-psvita-kit.sln -c Release
```

The tests run on both `net8.0` and `net10.0`, so the .NET 8 runtime is needed for the first.

## License

MIT. See [LICENSE](LICENSE).
