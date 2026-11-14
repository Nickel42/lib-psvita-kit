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
2. **Done: zRIF decoding.** `Zrif.Decode`, `TryDecode` and `ContentIdOf`. .NET's inflater takes no preset dictionary, so the dictionary goes in front of the stream body as a stored deflate block and is dropped from the output. Checks follow pkg2zip: header, deflate method, dictionary ID `0x627D1D5D`, Adler-32 (allowing the zero padding rif2zrif.py adds), size 512 (Vita) or 1024 (PSM). Tested against strings Python's zlib makes from made-up RIFs (`reference/python/gen_zrif_vectors.py`).
   **Still to do: `Zrif.Encode`.** Compressing against a preset dictionary needs a small deflate encoder of the kit's own (for example, LZ77 over dictionary + RIF with fixed Huffman codes). The result would decode the same, but its bytes would differ from rif2zrif.py's. Only needed to turn a `work.bin` into a zRIF.
3. **Done: PKG → NoNpDrm folder.** `PkgFile` reads the metadata (content type 0x15 game, 0x16 DLC, 0x18/0x1D PSM; category `gp` makes a game an update) and the unencrypted `param.sfo` copy without keys. `VitaConverter.ExtractPkgAsync` decrypts the item table, names and files (AES-128-CTR; content key = AES-ECB of the IV under `pkg_vita_N`) and writes the same tree as `pkg2zip -x`:
   - `app/TITLEID`, `addcont/TITLEID/LABEL`, `patch/TITLEID`, `psm/TITLEID`;
   - `digs.bin` kept unencrypted as `sce_sys/package/body.bin`; `head.bin` (file start to the end of the item table), `tail.bin` (after the encrypted data), `stat.bin` (768 zero bytes), and `work.bin` from the zRIF;
   - PSM: `content/` → `RO/`, `RO/License/FAKE.rif`, `RW/Documents`, `RW/Temp`, `RW/System/content_id` and `pm.dat`.

   The zRIF must match the package's content ID (`MissingLicenseException` otherwise); updates need none. Item names are checked as safe relative paths. Output goes to `<root>.lib-psvita-kit.tmp` and is renamed when complete. PSP and PS1 packages are recognised but not unpacked.
   **Tested against** synthetic packages (`tests/…/PkgBuilder.cs`), encrypted by a separate reference CTR. Not yet checked against real packages or against pkg2zip's own output.
4. **Folder ↔ zip and `.vpk`.** Packing with `System.IO.Compression` and unpacking with path checks (no `..` or absolute entries).
5. **Verification.** Check a package's SHA-1/SHA-256 digests; report what a folder is missing (for example, no `work.bin`).

## Open decisions

- **Package keys.** For now the caller supplies them (`PkgKeys`), as lib-3ds-kit does with `aes_keys.txt`. pkg2zip compiles them into its source instead; bundling them would be a one-line default in `PkgKeys`.
- **Reference implementation.** pkg2zip (public domain, Unlicense) is the reference for milestone 3. Its zRIF dictionary is already used and credited in `THIRD_PARTY_NOTICES.md`.
- **Test data.** Real packages can't be committed; `PkgBuilder` in the tests makes synthetic ones with made-up keys. A useful extra check: build pkg2zip with the test key swapped in and compare its `-x` output with the kit's on the same synthetic package.
