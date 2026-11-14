#!/usr/bin/env python3
"""Make zRIF test vectors with zlib itself, the way pkg2zip's rif2zrif.py does.

The RIFs are made up: a content ID where the console puts it (0x10 for a Vita RIF, 0x50 for a PSM
one) and a deterministic pattern for the rest. Prints C# for tests/lib-psvita-kit.Tests/ZrifVectors.cs.

usage: python gen_zrif_vectors.py
"""
import base64
import hashlib
import zlib

# zRIF's preset dictionary, as rif2zrif.py stores it (zlib-compressed, base64).
ZRIF_DICT = zlib.decompress(base64.b64decode(
    b"eNpjYBgFo2AU0AsYAIElGt8MRJiDCAsw3xhEmIAIU4N4AwNdRxcXZ3+/EJCAkW6Ac7C7ARwYgviuQAaIdoPSzlDaBUo7QmknIM3ACIZM78+u7kx3VWYEAGJ9HV0="))
assert zlib.adler32(ZRIF_DICT) == 0x627D1D5D


def fake_rif(size, content_id_offset, content_id, seed):
    rif = bytearray(size)
    rif[0:4] = b"\x00\x01\x00\x01"  # version and type, as NoNpDrm writes them
    rif[content_id_offset:content_id_offset + len(content_id)] = content_id.encode("ascii")
    pattern = hashlib.sha256(seed.encode()).digest() * 2  # 64 bytes standing in for key material
    start = 0x50 if size == 512 else 0x90
    rif[start:start + 64] = pattern
    return bytes(rif)


def zrif(rif):
    c = zlib.compressobj(level=9, wbits=10, memLevel=8, zdict=ZRIF_DICT)
    data = c.compress(rif) + c.flush()
    if len(data) % 3:
        data += b"\0" * (3 - len(data) % 3)  # rif2zrif.py pads to whole base64 groups
    return base64.b64encode(data).decode("ascii")


cases = [
    ("Vita", fake_rif(512, 0x10, "UP0000-PCSE00000_00-0000000000000000", "vita")),
    ("VitaDlc", fake_rif(512, 0x10, "EP9000-PCSF00001_00-ADDCONT000000002", "dlc")),
    ("Psm", fake_rif(1024, 0x50, "UP0001-NPNA00000_00-0000000000000001", "psm")),
]
print(f'    public const string DictionarySha256 = "{hashlib.sha256(ZRIF_DICT).hexdigest()}";')
print(f"    public const int DictionaryLength = {len(ZRIF_DICT)};")
for name, rif in cases:
    print(f'    public const string {name}Zrif = "{zrif(rif)}";')
    print(f'    public const string {name}RifSha256 = "{hashlib.sha256(rif).hexdigest()}";')
