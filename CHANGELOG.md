# Changelog

All notable changes are listed here. Versions follow [Semantic Versioning](https://semver.org/); while the version is 0.x, minor versions may change the API.

## Unreleased

### Added
- **`SfoFile`:** read and write `param.sfo`, with typed text and integer entries.
- **`TitleId` and `ContentId`:** parse and check title IDs (`PCSE00000`) and content IDs (`UP0000-PCSE00000_00-0000000000000000`).
- **`PkgHeader`:** read a `.pkg`'s header: content ID, sizes, IV and key type.
- **`VitaFile.Detect` and `Inspect`:** recognise `.pkg`, `.vpk`, NoNpDrm folders, Vita3K zips and `param.sfo`, and read their title and IDs.
- **zRIF decoding:** `Zrif.Decode` and `TryDecode` turn a zRIF string into its RIF (512 bytes for Vita, 1024 for PSM), with pkg2zip's checks; `Zrif.ContentIdOf` reads the content ID a RIF is for.
- **`psvita-kit` CLI:** `info`, `sfo` and `zrif` commands.
- **Stubs:** `Zrif.Encode` and `VitaConverter.ExtractPkgAsync` throw `NotImplementedException` until they're built.
