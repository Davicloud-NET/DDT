# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Writes the console's keyboard tables: presses every printing key of each layout through libxkbcommon with each mix
# of Shift, AltGr and Caps Lock and records what it types, then what its dead keys make. Runs in the container of the
# Dockerfile beside it, through build/Update-KeyboardLayouts.ps1, and rewrites the .keymap files in the folder given.

import ctypes
import pathlib
import subprocess
import sys
import unicodedata

# xkb's name of each layout and the file it goes to. KeyboardLayouts.cs maps the Windows ids onto these.
LAYOUTS = [
    ("be", "", "be"), ("br", "", "br"), ("ch", "", "ch"), ("ch", "fr", "ch-fr"), ("cz", "", "cz"),
    ("de", "", "de"), ("dk", "", "dk"), ("es", "", "es"), ("fi", "", "fi"), ("fr", "", "fr"), ("gb", "", "gb"),
    ("hu", "", "hu"), ("it", "", "it"), ("no", "", "no"), ("pl", "", "pl"), ("pt", "", "pt"), ("se", "", "se"),
    ("tr", "", "tr"), ("us", "", "us"), ("us", "intl", "us-intl"),
]

# evdev codes of the keys that type: the four rows, the 102nd key, space, and the keypad.
TYPING = [*range(2, 14), *range(16, 28), *range(30, 42), *range(43, 54), 57, 86]
KEYPAD = [55, *range(71, 84), 98]

SHIFT, CAPS, NUM, ALTGR = 42, 58, 69, 100
EVDEV_TO_XKB = 8

# The combining mark of each dead key, by its keysym.
DEAD = {
    0xFE50: 0x0300, 0xFE51: 0x0301, 0xFE52: 0x0302, 0xFE53: 0x0303, 0xFE54: 0x0304, 0xFE55: 0x0306,
    0xFE56: 0x0307, 0xFE57: 0x0308, 0xFE58: 0x030A, 0xFE59: 0x030B, 0xFE5A: 0x030C, 0xFE5B: 0x0327,
    0xFE5C: 0x0328, 0xFE5D: 0x0345, 0xFE60: 0x0323, 0xFE61: 0x0309, 0xFE62: 0x031B, 0xFE64: 0x0313,
    0xFE65: 0x0314, 0xFE66: 0x030F, 0xFE67: 0x0325, 0xFE68: 0x0331, 0xFE69: 0x032D, 0xFE6A: 0x0330,
    0xFE6B: 0x032E, 0xFE6C: 0x0324, 0xFE6D: 0x0311, 0xFE6E: 0x0326,
}
LEVEL3 = 0xFE03

# What a dead key types before a space, or before a letter it doesn't make anything with: the accents Windows types.
SPACING = {
    0x0300: 0x60, 0x0301: 0xB4, 0x0302: 0x5E, 0x0303: 0x7E, 0x0304: 0xAF, 0x0306: 0x2D8, 0x0307: 0x2D9,
    0x0308: 0xA8, 0x030A: 0xB0, 0x030B: 0x2DD, 0x030C: 0x2C7, 0x0327: 0xB8, 0x0328: 0x2DB,
}

# Where a layout's own accent differs, by combining mark: US International types ' and " as Windows does.
ACCENTS = {"us-intl": {0x0301: 0x27, 0x0308: 0x22}}

# A dead key makes only letters of the layout's Windows code page, as Windows' tables do: the upper half of it for
# Central European layouts, its accented letters from 0xC0 on for the others. Czech makes all of Latin Extended-A.
CODE_PAGES = {"cz": "latin", "hu": "cp1250", "pl": "cp1250", "tr": "cp1254"}

# Windows' own pairs, over what Unicode makes. Polish ~ makes the Polish letters and nothing else; Czech ˙ makes · and
# ı; US International ' and c make ç; Turkish İ and ı take accents as I and i do.
OWN = {
    "cz": {0x0307: {0x20: 0xB7, 0x69: 0x131}},
    "pl": {0x0303: dict(zip(map(ord, "AaCcEeLlNnOoSsXxZz"), map(ord, "ĄąĆćĘęŁłŃńÓóŚśŹźŻż")))},
    "us-intl": {0x0301: {0x43: 0xC7, 0x63: 0xE7}},
}
ONLY_OWN = {"pl": {0x0303}}
AS_LETTER = {"tr": {0x130: 0x49, 0x131: 0x69}}

# Keys where xkb's layout types something else than Windows' layout of the same name, so a password typed in Windows
# PE types the same here. By layout, evdev code and level (alone, Shift, AltGr, Shift+AltGr); Caps Lock alike. A value
# is a code point, or ^ and a combining mark for a dead key. The test against Windows lists what is left.
WINDOWS = {
    "be": {5: {2: 0x7B}, 6: {2: 0x5B}, 40: {3: "^301"}, 43: {3: "^300"}, 53: {3: "^303"}},
    "br": {46: {2: 0x20A2}},
    "ch": {2: {2: 0xA6}, 5: {2: 0xB0}, 6: {2: 0xA7}},
    "ch-fr": {2: {2: 0xA6}, 5: {2: 0xB0}, 6: {2: 0xA7}},
    "cz": {
        2: {2: 0x7E}, 3: {2: "^30c"}, 4: {2: "^302"}, 6: {2: "^30a"}, 9: {2: "^307"}, 11: {2: "^30b"},
        13: {2: "^327"}, 26: {2: 0xF7}, 27: {2: 0xD7}, 40: {2: 0xDF}, 43: {2: 0xA4},
    },
    "de": {12: {3: 0x1E9E}},
    "dk": {6: {2: 0x20AC}},
    "es": {5: {2: "^303"}, 6: {2: 0x20AC}},
    "fi": {6: {2: 0x20AC}},
    "fr": {3: {2: "^303"}, 8: {2: "^300"}},
    "gb": {
        18: {2: 0xE9, 3: 0xC9}, 22: {2: 0xFA, 3: 0xDA}, 23: {2: 0xED, 3: 0xCD}, 24: {2: 0xF3, 3: 0xD3},
        30: {2: 0xE1, 3: 0xC1}, 41: {2: 0xA6}, 43: {2: 0x5C, 3: 0x7C},
    },
    "hu": {4: {2: "^302"}},
    "it": {6: {2: 0x20AC}},
    "no": {6: {2: 0x20AC}},
    "pl": {22: {2: 0x20AC}, 41: {1: "^303"}, 86: {0: 0x5C, 1: 0x7C}},
    "pt": {6: {2: 0x20AC}, 27: {2: 0x5D}},
    "tr": {
        4: {1: "^302"}, 23: {2: 0x69, 3: 0x130}, 27: {2: "^303"}, 30: {2: 0xE6, 3: 0xC6}, 39: {2: "^301"},
        43: {2: "^300"},
    },
    "us": {86: {0: 0x5C, 1: 0x7C, 2: 0x5C, 3: 0x7C}},
    "us-intl": {40: {2: 0xB4, 3: 0xA8}},
}


class RuleNames(ctypes.Structure):
    _fields_ = [(name, ctypes.c_char_p) for name in ("rules", "model", "layout", "variant", "options")]


xkb = ctypes.CDLL("libxkbcommon.so.0")
xkb.xkb_context_new.restype = ctypes.c_void_p
xkb.xkb_keymap_new_from_names.restype = ctypes.c_void_p
xkb.xkb_keymap_new_from_names.argtypes = [ctypes.c_void_p, ctypes.POINTER(RuleNames), ctypes.c_int]
xkb.xkb_state_new.restype = ctypes.c_void_p
xkb.xkb_state_new.argtypes = [ctypes.c_void_p]
xkb.xkb_state_unref.argtypes = [ctypes.c_void_p]
xkb.xkb_state_update_key.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_int]
xkb.xkb_state_key_get_one_sym.restype = ctypes.c_uint32
xkb.xkb_state_key_get_one_sym.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
xkb.xkb_state_key_get_utf32.restype = ctypes.c_uint32
xkb.xkb_state_key_get_utf32.argtypes = [ctypes.c_void_p, ctypes.c_uint32]


def keymap(context, layout, variant):
    names = RuleNames(b"evdev", b"pc105", layout.encode(), variant.encode(), b"")
    result = xkb.xkb_keymap_new_from_names(context, ctypes.byref(names), 0)
    if not result:
        sys.exit(f"xkb has no layout {layout}({variant})")
    return result


def tap(state, code):
    xkb.xkb_state_update_key(state, code + EVDEV_TO_XKB, 1)
    xkb.xkb_state_update_key(state, code + EVDEV_TO_XKB, 0)


# What one key types with the modifiers held and the locks on, as a cell of the table.
def cell(table, code, shift, altgr, caps, num):
    state = xkb.xkb_state_new(table)
    if caps:
        tap(state, CAPS)
    if num:
        tap(state, NUM)
    if shift:
        xkb.xkb_state_update_key(state, SHIFT + EVDEV_TO_XKB, 1)
    if altgr:
        xkb.xkb_state_update_key(state, ALTGR + EVDEV_TO_XKB, 1)
    sym = xkb.xkb_state_key_get_one_sym(state, code + EVDEV_TO_XKB)
    text = xkb.xkb_state_key_get_utf32(state, code + EVDEV_TO_XKB)
    xkb.xkb_state_unref(state)
    # A dead key without a combining mark, like dead_greek, types nothing: Windows has none of them.
    if 0xFE50 <= sym <= 0xFE8F:
        return f"^{DEAD[sym]:x}" if sym in DEAD else "-"
    return "-" if text < 0x20 or 0x7F <= text < 0xA0 else f"{text:x}"


def is_level3(table):
    state = xkb.xkb_state_new(table)
    sym = xkb.xkb_state_key_get_one_sym(state, ALTGR + EVDEV_TO_XKB)
    xkb.xkb_state_unref(state)
    return sym == LEVEL3


def version(package):
    return subprocess.run(["dpkg-query", "--show", "--showformat", "${Version}", package],
                          check=True, capture_output=True, text=True).stdout


# The table's cells with the layout's Windows keys put in, at both Caps Lock states.
def windows(name, code, cells):
    for level, value in WINDOWS.get(name, {}).get(code, {}).items():
        cells[level] = cells[level + 4] = value if isinstance(value, str) else f"{value:x}"
    return cells


def code_page(name):
    page = CODE_PAGES.get(name, "cp1252")
    if page == "latin":
        return {chr(letter) for letter in range(0xC0, 0x180)}
    start = 0x80 if page == "cp1250" else 0xC0
    return set(bytes(range(start, 0x100)).decode(page, errors="ignore"))


# The dead lines of a layout: what each of its dead keys makes with each of its letters, and with a space.
def dead_lines(name, marks, letters):
    allowed = code_page(name)
    lines = []
    for mark in sorted(marks):
        made = {0x20: ACCENTS.get(name, {}).get(mark, SPACING.get(mark, mark))}
        for letter in [] if mark in ONLY_OWN.get(name, set()) else sorted(letters):
            base = AS_LETTER.get(name, {}).get(letter, letter)
            composed = unicodedata.normalize("NFC", chr(base) + chr(mark))
            if len(composed) == 1 and composed in allowed:
                made[letter] = ord(composed)
        made.update(OWN.get(name, {}).get(mark, {}))
        lines.extend(f"dead {mark:x} {letter:x} {result:x}" for letter, result in sorted(made.items()))
    return lines


def write(path, lines):
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def main(out):
    context = xkb.xkb_context_new(0)
    source = f"xkb-data {version('xkb-data')} with libxkbcommon {version('libxkbcommon0')}"
    for layout, variant, name in LAYOUTS:
        table = keymap(context, layout, variant)
        level3 = is_level3(table)
        full = f"{layout}({variant})" if variant else layout
        lines = [
            f"# DDT keyboard layout {full}, sampled from {source}.",
            "# Written by make_layouts.py, do not edit. A line is an evdev code and what the key types with nothing,",
            "# Shift, AltGr, Shift+AltGr, then the same four with Caps Lock on. A cell is a code point in hex, - for",
            "# nothing, or ^ and the combining mark of a dead key. The keypad is sampled with Num Lock on.",
            "# A dead line is a combining mark, the key after it and what the two make, in hex; 20 is a space, which",
            f"# gives the accent alone. Letters from Unicode {unicodedata.unidata_version}.",
            f"altgr {1 if level3 else 0}",
        ]
        marks, letters = set(), set()
        for code in TYPING + KEYPAD:
            num = code in KEYPAD
            sampled = [cell(table, code, shift, altgr and level3, caps, num)
                       for caps in (False, True) for altgr in (False, True) for shift in (False, True)]
            cells = windows(name, code, sampled)
            for value in cells:
                if value.startswith("^"):
                    marks.add(int(value[1:], 16))
                elif value != "-":
                    letters.add(int(value, 16))
            lines.append(f"{code} {' '.join(cells)}")
        write(out / f"{name}.keymap", lines + dead_lines(name, marks, letters))


if __name__ == "__main__":
    main(pathlib.Path(sys.argv[1]))
