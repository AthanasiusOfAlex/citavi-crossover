# Fonts copied into the bottle

`citavi-crossover fonts` copies every `.ttf` file in this folder (not in `source/`) into the
bottle's `C:\windows\Fonts`. `patches/registry.conf` then tells Wine to use them wherever Citavi
asks for Segoe UI.

| Files | Font | Made from | Licence |
|---|---|---|---|
| `bottleui.ttf`, `bottleuib.ttf`, `bottleuil.ttf`, `bottleuisl.ttf`, `bottleuisb.ttf` | Bottle UI, upright: regular, bold, light, semilight, semibold | the fonts in `source/`, by `build-fonts.py` | SIL Open Font License 1.1 |
| `bottleuii.ttf`, `bottleuiz.ttf`, `bottleuili.ttf`, `bottleuisli.ttf`, `bottleuisbi.ttf` | Bottle UI, italic: the same five weights | the fonts in `source/`, by `build-fonts.py` | SIL Open Font License 1.1 |
| `source/selawk.ttf`, `selawkb.ttf`, `selawkl.ttf`, `selawksl.ttf`, `selawksb.ttf` | Selawik 1.01, unchanged | `Selawik_Release.zip` from https://github.com/microsoft/Selawik/releases/tag/1.01 | SIL Open Font License 1.1 |
| `source/OpenSans-Light.ttf`, `-Regular`, `-Bold`, `source/OFL-OpenSans.txt` | Open Sans 3.003, unchanged, and its licence text | `fonts/ttf/` and `OFL.txt` in https://github.com/googlefonts/opensans (branch `main`, fetched 2026-11-17) | SIL Open Font License 1.1 |
| `source/NotoSans-Light.ttf`, `-Regular`, `-SemiBold`, `-Bold`, the four `…Italic.ttf`, `source/OFL-NotoSans.txt` | Noto Sans 2.015, unchanged, and its licence text | `fonts/NotoSans/unhinted/ttf/` in https://github.com/notofonts/notofonts.github.io and `OFL.txt` in https://github.com/notofonts/latin-greek-cyrillic (branch `main`, fetched 2026-11-17) | SIL Open Font License 1.1 |

Selawik is Microsoft's open-source fallback for Segoe UI, with the same letter widths and line
heights. That is why it stays the font of everything upright and Latin: Citavi's windows were laid
out for those widths.

## Why the fonts are rebuilt

Selawik alone has two gaps.

**Missing characters.** Selawik has only Latin letters, 349 characters in all. Citavi's text
fields are rich edit controls. Wine's rich edit control, and Windows' `DrawText` under Wine, draw
a character the font lacks as a blank 0.65 em wide; they cannot take it from another font. So a
Greek word in a title was a row of gaps, and the no-break space that Citavi itself puts between
first and last name in a task ("@Ada Lovelace") was more than twice as wide as a space.

**No italics.** Wine slants the upright letters where Citavi asks for italic, as it does for the
title on the Overview tab. Slanted upright letters are not an italic and look it.

`build-fonts.py` fills both.

### The upright fonts

A few characters get the glyph of a Selawik character that looks the same:

| Character | Gets the glyph of |
|---|---|
| U+00A0 no-break space, U+202F narrow no-break space, U+2009 thin space | space |
| U+2010 hyphen, U+2011 non-breaking hyphen | hyphen-minus |
| U+02BC modifier letter apostrophe | right single quotation mark |

The two narrow spaces come out as wide as a normal space.

Every other character is copied from two fonts, in this order:

1. Greek, modern and ancient (polytonic), from Noto Sans: 354 characters. Open Sans has modern
   Greek only, so both come from Noto Sans and every Greek word is in one design.
2. Whatever Open Sans has: 583 characters, among them Cyrillic, Hebrew, the ordinal signs º and
   ª and more accented Latin letters.
3. Whatever only Noto Sans has: 1672 characters, among them the dotted letters used to
   transliterate Hebrew and Arabic (ḥ, ṭ, ṣ), phonetic letters and more Cyrillic.

Each font then has 2963 characters. The copies are scaled so that their small and capital letters
are about as tall as Selawik's, and they come from the weight whose strokes are closest in width:

| Bottle UI | Selawik | Open Sans | Noto Sans |
|---|---|---|---|
| Light | Light | Light | Light |
| Semilight | Semilight | Light | Light |
| Regular | Regular | Regular | Regular |
| Semibold | Semibold | Bold | SemiBold |
| Bold | Bold | Bold | Bold |

Selawik's own outlines, widths and line heights are unchanged, so Latin text looks and measures
as before. The copied glyphs carry no hinting and no kerning.

### The italic fonts

Each weight gets an italic face made of Noto Sans Italic (Light Italic for light and semilight,
Italic, SemiBold Italic, Bold Italic), scaled the same way, with Selawik's line heights so that a
line does not change height when part of it is italic. What Noto Sans Italic lacks comes upright
from Open Sans: 87 characters, Hebrew among them. Italic Latin text is a little narrower than the
slanted Selawik it replaces.

### Names and licences

All three fonts are under the SIL Open Font License 1.1 (`source/OFL-OpenSans.txt`,
`source/OFL-NotoSans.txt`; Selawik's release has no licence file, its fonts name the licence in
their name tables). It reserves the name Selawik for the unchanged fonts, so the results carry
another name, Bottle UI. Their name tables keep Selawik's copyright and licence notices and add
those of Open Sans and Noto Sans.

### Still missing

- Arabic, Chinese, Japanese, Korean and every other script not named above, and arrows. In text
  fields these are blank.
- Hebrew vowel points are copied without the rules that position them over their letters; how
  pointed Hebrew looks was not tested.

Adding a script needs glyphs from a further openly licensed font: put its files in `source/` and
add a step to `build()` in the script.

## Changing the fonts

After changing `build-fonts.py`, raise `REVISION` in it and run:

```bash
python3 fonts/build-fonts.py
```

```bash
./citavi-crossover fonts
```

The script needs only Python 3. Quit Citavi before `fonts`; new fonts are used from the next start.

To use a different font altogether, put its files here, change the right-hand names of the
`segoe-ui…` lines in `patches/registry.conf`, and run `./citavi-crossover fonts` and
`./citavi-crossover registry`.
