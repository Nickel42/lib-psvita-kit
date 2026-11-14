# Changelog

All notable changes are listed here. Versions follow [Semantic Versioning](https://semver.org/); while the version is 0.x, minor versions may change the API.

## Unreleased

### Added
- **`SfoFile`:** read and write `param.sfo`, with typed text and integer entries.
- **`TitleId` and `ContentId`:** parse and check title IDs (`PCSE00000`) and content IDs (`UP0000-PCSE00000_00-0000000000000000`).
- **`PkgHeader`:** read a `.pkg`'s header: content ID, sizes, IV and key type.
- **`VitaFile.Detect` and `Inspect`:** recognise `.pkg`, `.vpk`, NoNpDrm folders, Vita3K zips and `param.sfo`, and read their title and IDs.
- **`psvita-kit` CLI:** `info` and `sfo` commands.
- **Stubs:** `Zrif.Decode`/`Encode` and `VitaConverter.ExtractPkgAsync` throw `NotImplementedException` until they're built.
