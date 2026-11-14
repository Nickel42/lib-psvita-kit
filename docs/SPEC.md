# lib-psvita-kit: specification

What the kit does and the order it gets there. The kit is meant for any .NET app, so nothing here assumes a particular frontend or emulator beyond naming the formats they read.

## Scope

**In:** PlayStation Store packages with their NoNpDrm licenses, decrypted game folders, and the zips and `.vpk`s that hold them. Games, DLC (`addcont`), updates (`patch`) and themes where the package format is shared.

**Out, for now:** `.psvimg`/`.psvmd` backups and PFS decryption (both need the console's own keys), Maidump and other dump layouts, PSP and PSM packages on the Vita store, and anything that writes to a console. Launcher stub files (such as `.psvita`) belong to the app that writes them: the kit gives it the title and title ID through `VitaFile.Inspect`.

## Formats

| Format | Notes |
|---|---|
| `.pkg` | Big-endian header: magic `7F 50 4B 47`, revision and type at 0x04/0x06, metadata offset and count at 0x08/0x0C, item count 0x14, total size 0x18, encrypted data offset and size 0x20/0x28, content ID (36 bytes) 0x30, IV 0x70. The extended header gives the key type at 0xE7 (low 3 bits). The body is AES-128-CTR; the item table names each file. |
| zRIF | Base64 of a zlib stream compressed against a fixed preset dictionary; decodes to the 512-byte RIF written as `sce_sys/package/work.bin`. |
| NoNpDrm folder | `TITLEID/` with `eboot.bin`, `sce_sys/param.sfo` and, when licensed, `sce_sys/package/work.bin`. DLC goes under `addcont/TITLEID/<label>/`, updates under `patch/TITLEID/`. |
| Vita3K zip | A zip of the NoNpDrm folder; Vita3K's File › Install .zip takes it. |
| `.vpk` | A zip with `eboot.bin` and `sce_sys/param.sfo` at its root (homebrew). |
| `param.sfo` | PSF 1.1, little-endian: header, 16-byte index entries, key table (sorted, 4-byte aligned), data table (each value padded to its max length). |

## Milestones

1. **Done: scaffold.** `SfoFile` (read/write), `TitleId`/`ContentId`, `PkgHeader`, `VitaFile.Detect`/`Inspect`, the `psvita-kit info` and `sfo` commands, tests built from synthetic files.
2. **zRIF.** `Zrif.Decode`/`Encode`. .NET's `ZLibStream` takes no preset dictionary; one route is to put the dictionary in a stored deflate block in front of the stream body and drop that many bytes from the output. Test against known zRIF ↔ work.bin pairs.
3. **PKG → NoNpDrm folder.** Metadata (content type: game, DLC, update, theme), item table, AES-CTR body decryption, writing the tree and `work.bin`, checking the RIF's content ID against the package. Streaming, with progress and cancellation; temporary output named `*.lib-psvita-kit.tmp` until it completes.
4. **Folder ↔ zip and `.vpk`.** Packing with `System.IO.Compression` and unpacking with path checks (no `..` or absolute entries).
5. **Verification.** Check a package's SHA-1/SHA-256 digests; report what a folder is missing (for example, no `work.bin`).

## Open decisions

- **Package keys.** pkg2zip compiles the PS Vita package keys into its source. Decide whether the kit does the same or takes them from the caller, as lib-3ds-kit does with `aes_keys.txt`.
- **Reference implementation.** pkg2zip is the reference for milestones 2 and 3. Check its license before porting code, and list it in `THIRD_PARTY_NOTICES.md` if any is used.
- **Test data.** Real packages can't be committed. Plan a small generator for synthetic `.pkg` files encrypted with test keys, as lib-3ds-kit has for its fixtures.
