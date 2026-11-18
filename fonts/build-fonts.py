#!/usr/bin/env python3
"""Builds the fonts in this folder from the Selawik fonts in source/.

Selawik has only Latin letters, and lacks a few characters that Segoe UI has and that Citavi uses,
such as the no-break space. Wine's rich edit control, which Citavi's text fields are, draws a
missing character as a blank 0.65 em wide. This script fills the gaps in two ways:

- A few characters get the glyph of a Selawik character that looks the same (see ALIASES).
- Every other character is copied from two openly licensed fonts in source/ (see DONORS): Greek,
  modern and ancient, from Noto Sans; then whatever Open Sans has (Cyrillic, Hebrew, more Latin
  letters, symbols); then whatever only Noto Sans has. Each Selawik weight takes them from the
  weight closest in stroke width, scaled so that the letters are as tall as Selawik's.

Selawik's own outlines, widths and line heights stay as they are.

Selawik has no italics, and Wine slants the upright letters where a program asks for italic.
So each weight also gets a real italic face: Noto Sans Italic, scaled the same way and given
Selawik's line heights (see ITALICS), plus what only Open Sans has (Hebrew).

All three fonts are under the SIL Open Font License 1.1 (source/OFL-*.txt). It reserves the name
Selawik for the unmodified fonts, so the results are named FAMILY instead; they carry all three
copyright notices. patches/registry.conf tells Wine to use that family wherever Citavi asks for
Segoe UI.

Usage: python3 fonts/build-fonts.py        (needs nothing but Python 3)
       python3 fonts/build-fonts.py --family "Some Name" --out some/folder     (a trial build)
"""
import struct
import sys
from pathlib import Path

FAMILY = "Bottle UI"
ORIGINAL = "Selawik"
REVISION = 4  # raise when the output changes

# new character -> the character whose glyph it shares
ALIASES = {
    0x00A0: 0x0020,  # no-break space: Citavi joins first and last name with it ("@Ada Lovelace")
    0x202F: 0x0020,  # narrow no-break space (French punctuation); a little too wide, but not a hole
    0x2009: 0x0020,  # thin space; likewise
    0x2010: 0x002D,  # hyphen
    0x2011: 0x002D,  # non-breaking hyphen
    0x02BC: 0x2019,  # modifier letter apostrophe
}

# Selawik file -> the Open Sans and the Noto Sans file the other characters come from. Chosen by
# the width of the stem of "l" in font units, the donors after scaling:
#   Selawik    light  92, semilight 128, regular 164, semibold 265, bold 316
#   Open Sans  light  98,                regular 160, (semibold 226), bold 292
#   Noto Sans  light 112,                regular 173, semibold 249,  bold 295
DONORS = {
    "selawk.ttf": ("OpenSans-Regular.ttf", "NotoSans-Regular.ttf"),
    "selawkb.ttf": ("OpenSans-Bold.ttf", "NotoSans-Bold.ttf"),
    "selawkl.ttf": ("OpenSans-Light.ttf", "NotoSans-Light.ttf"),
    "selawksl.ttf": ("OpenSans-Light.ttf", "NotoSans-Light.ttf"),
    "selawksb.ttf": ("OpenSans-Bold.ttf", "NotoSans-SemiBold.ttf"),
}
# Selawik file -> the Noto Sans italic its italic face is made of (stems of "l" after scaling:
# light 111, regular 173, semibold 247, bold 291)
ITALICS = {
    "selawk.ttf": "NotoSans-Italic.ttf",
    "selawkb.ttf": "NotoSans-BoldItalic.ttf",
    "selawkl.ttf": "NotoSans-LightItalic.ttf",
    "selawksl.ttf": "NotoSans-LightItalic.ttf",
    "selawksb.ttf": "NotoSans-SemiBoldItalic.ttf",
}
# Greek and Greek Extended (the accented letters of ancient Greek). Open Sans has the first block
# only; taking both from Noto Sans keeps every Greek word in one design.
GREEK = ((0x0370, 0x03FF), (0x1F00, 0x1FFF))

# Unicode blocks and the bit each has in the OS/2 table's ulUnicodeRange (first, last, bit)
BLOCKS = [
    (0x0080, 0x00FF, 1), (0x0100, 0x017F, 2), (0x0180, 0x024F, 3), (0x0250, 0x02AF, 4), (0x02B0, 0x02FF, 5),
    (0x0300, 0x036F, 6), (0x0370, 0x03FF, 7), (0x0400, 0x052F, 9), (0x0590, 0x05FF, 11), (0x1E00, 0x1EFF, 29),
    (0x1F00, 0x1FFF, 30), (0x2000, 0x206F, 31), (0x2070, 0x209F, 32), (0x20A0, 0x20CF, 33), (0x2100, 0x214F, 35),
    (0x2150, 0x218F, 36), (0x2190, 0x21FF, 37), (0x2200, 0x22FF, 38), (0x25A0, 0x25FF, 45), (0xFB00, 0xFB4F, 62),
]
# a character whose presence means a Windows code page is covered -> its bit in ulCodePageRange
CODE_PAGES = {0x0411: 2, 0x03B1: 3, 0x05D0: 5, 0x1EA1: 8}

# source file -> output file of the italic face
ITALIC_FILES = {
    "selawk.ttf": "bottleuii.ttf",
    "selawkb.ttf": "bottleuiz.ttf",
    "selawkl.ttf": "bottleuili.ttf",
    "selawksl.ttf": "bottleuisli.ttf",
    "selawksb.ttf": "bottleuisbi.ttf",
}

# source file -> output file
FILES = {
    "selawk.ttf": "bottleui.ttf",
    "selawkb.ttf": "bottleuib.ttf",
    "selawkl.ttf": "bottleuil.ttf",
    "selawksl.ttf": "bottleuisl.ttf",
    "selawksb.ttf": "bottleuisb.ttf",
}


def read_tables(data):
    count = struct.unpack(">H", data[4:6])[0]
    tables = {}
    for i in range(count):
        tag, _, offset, length = struct.unpack(">4sIII", data[12 + 16 * i:28 + 16 * i])
        tables[tag.decode("latin-1")] = data[offset:offset + length]
    return data[:4], tables


def checksum(block):
    block += b"\0" * (-len(block) % 4)
    return sum(struct.unpack(">%dI" % (len(block) // 4), block)) & 0xFFFFFFFF


def write_font(version, tables):
    """Table directory, then the tables in tag order, each padded to four bytes."""
    tables = dict(tables)
    head = bytearray(tables["head"])
    head[8:12] = b"\0\0\0\0"  # checkSumAdjustment, filled in below
    tables["head"] = bytes(head)
    tags = sorted(tables)
    count = len(tags)
    power = 1 << (count.bit_length() - 1)
    header = version + struct.pack(">HHHH", count, power * 16, count.bit_length() - 1, count * 16 - power * 16)
    offset = 12 + 16 * count
    directory, body = b"", b""
    for tag in tags:
        table = tables[tag]
        directory += struct.pack(">4sIII", tag.encode("latin-1"), checksum(table), offset, len(table))
        padded = table + b"\0" * (-len(table) % 4)
        body += padded
        offset += len(padded)
    font = bytearray(header + directory + body)
    head_offset = 12 + 16 * count + sum(len(tables[t]) + (-len(tables[t]) % 4) for t in tags if t < "head")
    font[head_offset + 8:head_offset + 12] = struct.pack(">I", (0xB1B0AFBA - checksum(bytes(font))) & 0xFFFFFFFF)
    return bytes(font)


def read_cmap(cmap):
    """The Unicode mapping (Windows, format 4) as {character: glyph}."""
    for i in range(struct.unpack(">H", cmap[2:4])[0]):
        platform, encoding, offset = struct.unpack(">HHI", cmap[4 + 8 * i:12 + 8 * i])
        if (platform, encoding) == (3, 1) and struct.unpack(">H", cmap[offset:offset + 2])[0] == 4:
            break
    else:
        raise SystemExit("no Windows Unicode mapping in format 4")
    sub = cmap[offset:]
    segments = struct.unpack(">H", sub[6:8])[0] // 2
    def row(k):
        start = 14 + 2 * segments * k + (2 if k else 0)
        return struct.unpack(">%dH" % segments, sub[start:start + 2 * segments])
    ends, starts, deltas, range_offsets = row(0), row(1), row(2), row(3)
    range_table = 16 + 6 * segments
    mapping = {}
    for s in range(segments):
        for char in range(starts[s], ends[s] + 1):
            if char == 0xFFFF:
                continue
            if range_offsets[s] == 0:
                glyph = (char + deltas[s]) & 0xFFFF
            else:
                at = range_table + 2 * s + range_offsets[s] + 2 * (char - starts[s])
                glyph = struct.unpack(">H", sub[at:at + 2])[0]
                if glyph:
                    glyph = (glyph + deltas[s]) & 0xFFFF
            if glyph:
                mapping[char] = glyph
    return mapping


def write_cmap(mapping):
    """A cmap table with one format 4 subtable, listed for Unicode and for Windows."""
    runs = []  # [first character, last character, first glyph]
    for char in sorted(mapping):
        if runs and char == runs[-1][1] + 1 and mapping[char] == runs[-1][2] + char - runs[-1][0]:
            runs[-1][1] = char
        else:
            runs.append([char, char, mapping[char]])
    runs.append([0xFFFF, 0xFFFF, 0])
    n = len(runs)
    power = 1 << (n.bit_length() - 1)
    sub = struct.pack(">HHHHHHH", 4, 16 + 8 * n, 0, 2 * n, 2 * power, n.bit_length() - 1, 2 * n - 2 * power)
    sub += struct.pack(">%dH" % n, *(r[1] for r in runs)) + b"\0\0"
    sub += struct.pack(">%dH" % n, *(r[0] for r in runs))
    sub += struct.pack(">%dH" % n, *((r[2] - r[0]) & 0xFFFF if r[0] != 0xFFFF else 1 for r in runs))
    sub += struct.pack(">%dH" % n, *([0] * n))
    return struct.pack(">HH", 0, 2) + struct.pack(">HHI", 0, 3, 20) + struct.pack(">HHI", 3, 1, 20) + sub


def rename(name, note, also_copyright, italic=False):
    """The name table with the family renamed, the other copyright notices and a description
    (name 10); for an italic face, with the style names changed to match."""
    count, strings = struct.unpack(">HH", name[2:6])
    records = []
    for i in range(count):
        platform, encoding, language, name_id, length, offset = struct.unpack(">6H", name[6 + 12 * i:18 + 12 * i])
        raw = name[strings + offset:strings + offset + length]
        codec = "utf-16-be" if platform in (0, 3) else "mac-roman"
        text = raw.decode(codec)
        if name_id == 6:      # PostScript name: no spaces
            text = text.replace(ORIGINAL, FAMILY.replace(" ", ""))
        elif name_id in (1, 3, 4, 16, 17, 18, 21, 22):
            text = text.replace(ORIGINAL, FAMILY)
        elif name_id == 0:
            text += " Portions " + also_copyright
        if italic and name_id == 6:
            text = text.replace("-Regular", "-") + "Italic"
        elif italic and name_id in (2, 3, 4, 17):
            text = (text[:-len("Regular")] if text.endswith("Regular") else text + " ") + "Italic"
        elif name_id == 5:
            text += "; %s %d" % (FAMILY, REVISION)
        elif name_id == 10:
            continue
        records.append((platform, encoding, language, name_id, text.encode(codec)))
    records.append((3, 1, 0x409, 10, note.encode("utf-16-be")))
    records.sort(key=lambda r: r[:4])
    table, pool = struct.pack(">HHH", 0, len(records), 6 + 12 * len(records)), b""
    for platform, encoding, language, name_id, raw in records:
        table += struct.pack(">6H", platform, encoding, language, name_id, len(raw), len(pool))
        pool += raw
    return table + pool


class Glyphs:
    """The outlines and widths of a font, by glyph number."""

    def __init__(self, tables):
        self.long = struct.unpack(">h", tables["head"][50:52])[0] == 1
        self.count = struct.unpack(">H", tables["maxp"][4:6])[0]
        loca, self.glyf = tables["loca"], tables["glyf"]
        if self.long:
            self.offsets = struct.unpack(">%dI" % (self.count + 1), loca[:4 * self.count + 4])
        else:
            self.offsets = [2 * o for o in struct.unpack(">%dH" % (self.count + 1), loca[:2 * self.count + 2])]
        full = struct.unpack(">H", tables["hhea"][34:36])[0]  # glyphs with a width of their own
        hmtx = tables["hmtx"]
        self.metrics = [struct.unpack(">Hh", hmtx[4 * i:4 * i + 4]) for i in range(full)]
        for i in range(full, self.count):
            self.metrics.append((self.metrics[full - 1][0], struct.unpack(">h", hmtx[2 * (full + i):2 * (full + i) + 2])[0]))

    def data(self, glyph):
        return self.glyf[self.offsets[glyph]:self.offsets[glyph + 1]]


def scaled_simple(data, k):
    """A glyph made of contours, its points multiplied by k and its hinting program removed."""
    contours = struct.unpack(">h", data[:2])[0]
    ends = struct.unpack(">%dH" % contours, data[10:10 + 2 * contours])
    points = ends[-1] + 1
    at = 10 + 2 * contours
    at += 2 + struct.unpack(">H", data[at:at + 2])[0]
    flags = []
    while len(flags) < points:
        flag = data[at]
        at += 1
        flags.append(flag)
        if flag & 8:  # repeated
            flags.extend([flag] * data[at])
            at += 1
    axes = []
    for short, same in ((2, 16), (4, 32)):  # x, then y; stored as steps from point to point
        value, values = 0, []
        for flag in flags:
            if flag & short:
                value += data[at] if flag & same else -data[at]
                at += 1
            elif not flag & same:
                value += struct.unpack(">h", data[at:at + 2])[0]
                at += 2
            values.append(int(round(value * k)))
        axes.append(values)
    xs, ys = axes
    out = struct.pack(">5h", contours, min(xs), min(ys), max(xs), max(ys))
    out += struct.pack(">%dH" % contours, *ends) + struct.pack(">H", 0)
    out += bytes(flag & 0x41 for flag in flags)  # on-curve and overlap; every step written in full
    for values in (xs, ys):
        out += struct.pack(">%dh" % points, *(b - a for a, b in zip([0] + values, values)))
    return out, min(xs)


def scaled_composite(data, k, renumber):
    """A glyph made of other glyphs: those renumbered, its offsets multiplied by k, hinting removed."""
    box = struct.unpack(">4h", data[2:10])
    out = struct.pack(">5h", -1, *(int(round(v * k)) for v in box))
    at = 10
    while True:
        flags, part = struct.unpack(">HH", data[at:at + 4])
        at += 4
        by_offset = flags & 2
        if flags & 1:
            first, second = struct.unpack(">hh" if by_offset else ">HH", data[at:at + 4])
            at += 4
        else:
            first, second = struct.unpack(">bb" if by_offset else ">BB", data[at:at + 2])
            at += 2
        size = 2 if flags & 0x08 else 4 if flags & 0x40 else 8 if flags & 0x80 else 0
        transform = data[at:at + size]
        at += size
        if by_offset:
            first, second = int(round(first * k)), int(round(second * k))
        out += struct.pack(">HH", (flags | 1) & ~0x100, renumber(part))
        out += struct.pack(">hh" if by_offset else ">HH", first, second) + transform
        if not flags & 0x20:  # no more parts
            return out


def parts_of(data):
    """The glyph numbers a composite glyph is made of."""
    at, parts = 10, []
    while True:
        flags, part = struct.unpack(">HH", data[at:at + 4])
        parts.append(part)
        at += 4 + (4 if flags & 1 else 2) + (2 if flags & 0x08 else 4 if flags & 0x40 else 8 if flags & 0x80 else 0)
        if not flags & 0x20:
            return parts


def add_from_donor(tables, mapping, donor_tables, copied, only=None):
    """Copies every character the donor has and `mapping` lacks into `tables`; with `only`, just the
    characters in those ranges. `copied` (donor glyph -> new glyph) is kept between calls for one
    donor. Returns the characters."""
    base, donor = Glyphs(tables), Glyphs(donor_tables)
    height = lambda t: sum(struct.unpack(">hh", t["OS/2"][86:90]))  # x-height + capital height
    k = height(tables) / height(donor_tables)
    glyphs = [base.data(i) for i in range(base.count)]
    metrics = list(base.metrics)

    def copy(glyph):
        if glyph in copied:
            return copied[glyph]
        data = donor.data(glyph)
        width, bearing = donor.metrics[glyph]
        bearing = int(round(bearing * k))
        if not data or struct.unpack(">h", data[:2])[0] == 0:
            new = b""
        elif struct.unpack(">h", data[:2])[0] > 0:
            new, bearing = scaled_simple(data, k)
        else:
            for part in parts_of(data):
                copy(part)
            new = scaled_composite(data, k, lambda part: copied[part])
        copied[glyph] = len(glyphs)
        glyphs.append(new + b"\0" * (-len(new) % 4))
        metrics.append((int(round(width * k)), bearing))
        return copied[glyph]

    added = []
    for char, glyph in sorted(read_cmap(donor_tables["cmap"]).items()):
        if char in mapping or char < 0x20 or 0xE000 <= char <= 0xF8FF:
            continue
        if only and not any(first <= char <= last for first, last in only):
            continue
        mapping[char] = copy(glyph)
        added.append(char)

    offsets, glyf = [0], b""
    for data in glyphs:
        glyf += data
        offsets.append(len(glyf))
    tables["glyf"] = glyf
    tables["loca"] = struct.pack(">%dI" % len(offsets), *offsets)
    tables["hmtx"] = b"".join(struct.pack(">Hh", *m) for m in metrics)

    head = bytearray(tables["head"])
    head[50:52] = struct.pack(">h", 1)  # loca holds 32-bit offsets
    boxes = [struct.unpack(">4h", g[2:10]) for g in glyphs[base.count:] if g]
    old = struct.unpack(">4h", head[36:44])
    head[36:44] = struct.pack(">4h", min([old[0]] + [b[0] for b in boxes]), min([old[1]] + [b[1] for b in boxes]),
                              max([old[2]] + [b[2] for b in boxes]), max([old[3]] + [b[3] for b in boxes]))
    tables["head"] = bytes(head)
    hhea = bytearray(tables["hhea"])
    hhea[10:12] = struct.pack(">H", max(m[0] for m in metrics))
    hhea[34:36] = struct.pack(">H", len(glyphs))
    tables["hhea"] = bytes(hhea)
    maxp, theirs = bytearray(tables["maxp"]), donor_tables["maxp"]
    maxp[4:6] = struct.pack(">H", len(glyphs))
    for at in (6, 8, 10, 12, 28, 30):  # most points, contours, parts and nesting of any glyph
        maxp[at:at + 2] = max(bytes(maxp[at:at + 2]), theirs[at:at + 2])
    tables["maxp"] = bytes(maxp)

    os2 = bytearray(tables["OS/2"])
    ranges = list(struct.unpack(">4I", os2[42:58]))
    for first, last, bit in BLOCKS:
        if any(first <= char <= last for char in added):
            ranges[bit // 32] |= 1 << (bit % 32)
    os2[42:58] = struct.pack(">4I", *ranges)
    pages = struct.unpack(">I", os2[78:82])[0]
    for char, bit in CODE_PAGES.items():
        if char in mapping:
            pages |= 1 << bit
    os2[78:82] = struct.pack(">I", pages)
    os2[66:68] = struct.pack(">H", max(max(mapping), struct.unpack(">H", os2[66:68])[0]))  # last character
    tables["OS/2"] = bytes(os2)
    return added


def keep_only_first_glyph(tables):
    """Removes every glyph but the "missing character" one, and the tables that refer to glyphs."""
    old = Glyphs(tables)
    first = old.data(0) + b"\0" * (-len(old.data(0)) % 4)
    tables["glyf"] = first
    tables["loca"] = struct.pack(">II", 0, len(first))
    tables["hmtx"] = struct.pack(">Hh", *old.metrics[0])
    for name, at, value in (("head", 50, 1), ("hhea", 34, 1), ("maxp", 4, 1)):
        table = bytearray(tables[name])
        table[at:at + 2] = struct.pack(">H", value)
        tables[name] = bytes(table)
    for name in ("GDEF", "GPOS", "GSUB"):  # kerning and the like, by glyph number
        tables.pop(name, None)


def mark_italic(tables, donor_tables):
    """Sets the fields that tell programs the font is italic, with the donor's slant."""
    head = bytearray(tables["head"])
    head[44:46] = struct.pack(">H", struct.unpack(">H", head[44:46])[0] | 2)       # macStyle: italic
    tables["head"] = bytes(head)
    os2 = bytearray(tables["OS/2"])
    os2[62:64] = struct.pack(">H", struct.unpack(">H", os2[62:64])[0] & ~0x40 | 1)  # fsSelection: italic, not regular
    tables["OS/2"] = bytes(os2)
    post = bytearray(tables["post"])
    post[4:8] = donor_tables["post"][4:8]                                            # italicAngle
    tables["post"] = bytes(post)
    hhea = bytearray(tables["hhea"])
    hhea[18:22] = donor_tables["hhea"][18:22]                                        # slope of the text cursor
    tables["hhea"] = bytes(hhea)


def name_of(tables, name_id):
    name = tables["name"]
    count, strings = struct.unpack(">HH", name[2:6])
    for i in range(count):
        platform, _, _, this, length, offset = struct.unpack(">6H", name[6 + 12 * i:18 + 12 * i])
        if platform == 3 and this == name_id:
            return name[strings + offset:strings + offset + length].decode("utf-16-be")
    return ""


def build(source, donors, target, italic=None):
    """Writes one font. With `italic`, the italic face: that Noto Sans italic instead of Selawik's glyphs."""
    version, tables = read_tables(source.read_bytes())
    open_sans, noto_sans = (read_tables(donor.read_bytes())[1] for donor in donors)
    described = lambda t: "%s (%s)" % (name_of(t, 4), name_of(t, 5))
    if italic:
        noto_sans = read_tables(italic.read_bytes())[1]
        keep_only_first_glyph(tables)
        mapping = {}
        from_noto = add_from_donor(tables, mapping, noto_sans, {})
        from_open = add_from_donor(tables, mapping, open_sans, {})
        mark_italic(tables, noto_sans)
        note = ("The italic of %s: %d characters from %s and %d from %s (upright), scaled, with the line heights of %s 1.01."
                % (FAMILY, len(from_noto), described(noto_sans), len(from_open), described(open_sans), ORIGINAL))
        counts = (len(from_noto), len(from_open))
    else:
        mapping = read_cmap(tables["cmap"])
    aliases = []
    for char, like in ALIASES.items():
        if char not in mapping and like in mapping:
            mapping[char] = mapping[like]
            aliases.append("U+%04X" % char)
    if not italic:
        copied = {}
        greek = add_from_donor(tables, mapping, noto_sans, copied, GREEK)
        from_open = add_from_donor(tables, mapping, open_sans, {})
        rest = add_from_donor(tables, mapping, noto_sans, copied)
        note = ("%s 1.01 with %s added as copies of existing glyphs, %d characters added from %s and %d from %s, scaled. "
                "Renamed because the SIL Open Font License reserves the name %s for the unmodified font."
                % (ORIGINAL, ", ".join(aliases), len(from_open), described(open_sans), len(greek) + len(rest), described(noto_sans), ORIGINAL))
        counts = (len(aliases), len(greek), len(from_open), len(rest))
    tables["cmap"] = write_cmap(mapping)
    tables["name"] = rename(tables["name"], note, name_of(open_sans, 0) + " Portions " + name_of(noto_sans, 0), bool(italic))
    tables.pop("DSIG", None)  # Microsoft's signature no longer matches the file
    target.write_bytes(write_font(version, tables))
    return (len(mapping),) + counts


def main():
    global FAMILY
    here = Path(__file__).resolve().parent
    out, args = here, sys.argv[1:]
    while args:
        option = args.pop(0)
        if option == "--family" and args:
            FAMILY = args.pop(0)
        elif option == "--out" and args:
            out = Path(args.pop(0))
        else:
            raise SystemExit(__doc__)
    for source, target in FILES.items():
        donors = [here / "source" / donor for donor in DONORS[source]]
        counts = build(here / "source" / source, donors, out / target)
        print("%-13s -> %-15s %4d characters: %d aliases, Greek %d from %s, %d from %s, %d more from Noto Sans"
              % ((source, target) + counts[:3] + (DONORS[source][1], counts[3], DONORS[source][0], counts[4])))
    for source, target in ITALIC_FILES.items():
        donors = [here / "source" / donor for donor in DONORS[source]]
        counts = build(here / "source" / source, donors, out / target, here / "source" / ITALICS[source])
        print("%-13s -> %-15s %4d characters: %d from %s, %d from %s"
              % ((source, target) + counts[:2] + (ITALICS[source], counts[2], DONORS[source][0])))


if __name__ == "__main__":
    sys.exit(main())
