# Changelog

All notable changes are listed here. Versions follow [Semantic Versioning](https://semver.org/); while the version is 0.x, minor versions may change the API.

## Unreleased

### Added
- **`SfoFile`:** read and write `param.sfo`, with typed text and integer entries.
- **`TitleId` and `ContentId`:** parse and check title IDs (`PCSE00000`) and content IDs (`UP0000-PCSE00000_00-0000000000000000`).
- **`PkgHeader`:** read a `.pkg`'s header: content ID, sizes, IV and key type.
- **`VitaFile.Detect` and `Inspect`:** recognise `.pkg`, `.vpk`, NoNpDrm folders, Vita3K zips and `param.sfo`, and read their title and IDs.
- **zRIF decoding:** `Zrif.Decode` and `TryDecode` turn a zRIF string into its RIF (512 bytes for Vita, 1024 for PSM), with pkg2zip's checks; `Zrif.ContentIdOf` reads the content ID a RIF is for.
- **PKG → NoNpDrm folder:** `VitaConverter.ExtractPkgAsync` unpacks Vita games, DLC, updates and PSM titles as `pkg2zip -x` does, with the zRIF checked against the package, progress, cancellation, and a temporary folder renamed when complete.
- **`PkgFile`:** a package's kind, title, IDs and `param.sfo` without keys; `ReadItems` for its file list with the key. `VitaFile.Inspect` now shows a package's title and kind.
- **`PkgKeys`:** the `pkg_vita_2/3/4` package keys. `PkgKeys.BuiltIn()` has all three and `new VitaConverter()` uses them; callers can set their own or load a text file, alone (`Load`) or over the built-in ones (`BuiltInWith`). `MissingKeyException` names a missing one.
- **`psvita-kit` CLI:** `info`, `sfo`, `zrif` and `extract` commands; `extract` uses the built-in keys unless `--keys` gives a file.
- **`IVitaEntrySink`:** `ExtractPkgAsync(PkgFile, zrif, sink)` unpacks into a caller's sink instead of a folder, so a package can go straight into a zip without a temporary folder. Same entries and bytes as the folder.
- **Stub:** `Zrif.Encode` throws `NotImplementedException` until it's built.

### Changed
- **Faster unpacking:** AES-CTR builds its counters as one 128-bit number, encrypts them in place and XORs with vector instructions (about 4x the decrypt speed). `ExtractPkgAsync` reads the next 1 MiB while it decrypts and writes the last, reads by position with sequential-scan hints, allocates each file at its full size up front, and creates each folder once. `PkgFile.ReadItems` reads and decrypts all the names in one go. Output is unchanged.
- **`ExtractPkgAsync(PkgFile, …)`:** takes a package already opened with `PkgFile.Open`, so callers that have one don't read its header again.
