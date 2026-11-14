# Third-party notices

## pkg2zip

The zRIF preset dictionary in `src/lib-psvita-kit/Licenses/Zrif.cs` (and in `reference/python/gen_zrif_vectors.py`) is taken from pkg2zip's `rif2zrif.py`, and the decoder's checks follow `pkg2zip_zrif.c`. `VitaConverter.ExtractPkgAsync` follows `pkg2zip.c`'s package layout and output tree, rewritten in C#. No keys are copied.

- Project: https://github.com/mmozeiko/pkg2zip
- License: The Unlicense (public domain dedication):

> This is free and unencumbered software released into the public domain.
>
> Anyone is free to copy, modify, publish, use, compile, sell, or distribute this software, either in source code form or as a compiled binary, for any purpose, commercial or non-commercial, and by any means.
