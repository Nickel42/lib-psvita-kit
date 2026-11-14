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
- **`PkgKeys`:** the caller's `pkg_vita_2/3/4` keys, set directly or loaded from a text file. No keys are bundled. `MissingKeyException` names a missing one.
- **`psvita-kit` CLI:** `info`, `sfo`, `zrif` and `extract` commands.
- **Stub:** `Zrif.Encode` throws `NotImplementedException` until it's built.
