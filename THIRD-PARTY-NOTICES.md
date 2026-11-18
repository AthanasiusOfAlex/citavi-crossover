# Third-party notices

The MIT licence in [LICENSE](LICENSE) covers what this project wrote: the `citavi-crossover`
script, the patcher, the helpers in `dev/` and `login-helper/`, the patch rules, the font build
script and the documentation. The files below come from others and keep their own licences.

## Fonts (`fonts/`)

All under the SIL Open Font License 1.1. [fonts/README.md](fonts/README.md) says which file
comes from where.

| Files | Font | Copyright | Licence text |
|---|---|---|---|
| `fonts/source/selawk*.ttf` | Selawik 1.01 | Microsoft Corporation | named in the fonts' name tables |
| `fonts/source/OpenSans-*.ttf` | Open Sans 3.003 | The Open Sans Project Authors | `fonts/source/OFL-OpenSans.txt` |
| `fonts/source/NotoSans-*.ttf` | Noto Sans 2.015 | The Noto Project Authors | `fonts/source/OFL-NotoSans.txt` |
| `fonts/bottleui*.ttf` | Bottle UI, built from the three fonts above | as above | SIL Open Font License 1.1, as the licence requires for a modified font |

## Libraries next to the ready-built patcher (`prebuilt/patcher/`)

All under the MIT licence, with the same permission text as in [LICENSE](LICENSE).

| Files | Library | Copyright |
|---|---|---|
| `Mono.Cecil.dll`, `Mono.Cecil.Mdb.dll`, `Mono.Cecil.Pdb.dll`, `Mono.Cecil.Rocks.dll` | Mono.Cecil 0.11.6 | Copyright (c) 2008 - 2015 Jb Evain; Copyright (c) 2008 - 2011 Novell, Inc. |
| `System.Text.Json.dll`, `System.Text.Encodings.Web.dll`, `Microsoft.Bcl.AsyncInterfaces.dll`, `System.Buffers.dll`, `System.Memory.dll`, `System.Numerics.Vectors.dll`, `System.Runtime.CompilerServices.Unsafe.dll`, `System.Threading.Tasks.Extensions.dll`, `System.ValueTuple.dll` | System.Text.Json 8.0.5 and the .NET packages it needs | Copyright (c) .NET Foundation and Contributors |

## Not part of this project

Citavi, CrossOver and the .NET Framework are not in this repository and are not covered by its
licence. The tool downloads their installers from their makers; their own licence terms apply.
