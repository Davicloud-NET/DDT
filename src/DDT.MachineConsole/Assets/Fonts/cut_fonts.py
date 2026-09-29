# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Cuts a static font of Archivo and Martian Mono for each weight and width that tokens.json uses, because the console
# can't vary a font's axes. Each font gets the name Tokens.axaml uses, like "Archivo 750 62", and tabular digits.
# Needs fonttools (pip install fonttools) and rewrites the .ttf files next to this script.

import hashlib
import io
import json
import pathlib
import urllib.request

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

HERE = pathlib.Path(__file__).resolve().parent
TOKENS = HERE.parents[2] / "DDT.Design" / "tokens.json"

# The google/fonts commits the sources are taken from, and what they must hash to.
SOURCES = {
    "sans": (
        "Archivo",
        "https://raw.githubusercontent.com/google/fonts/95f4904fc8bcf26d3420fe315560c96417c6dec7/ofl/archivo/Archivo%5Bwdth,wght%5D.ttf",
        "0e094a7d3c7c4c25cf1310c4b30014f1dae9332220b1c2c88f4fa996f0b05053",
    ),
    "mono": (
        "Martian Mono",
        "https://raw.githubusercontent.com/google/fonts/c8bba5c4a69195e4fabc69d75136814c65fe0cf5/ofl/martianmono/MartianMono%5Bwdth,wght%5D.ttf",
        "c3467843ec1c2574b05fbcfd7147c7bfbcf63ddca8fc2bcb9d117f1bfb1b22e7",
    ),
}

# Latin, Latin-1, Latin Extended A and B and Additional, combining marks, general punctuation, the euro sign, arrows.
UNICODES = (
    "U+0000-024F,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0300-0304,U+0308,U+0329,U+1E00-1EFF,U+2000-206F,U+20AC,"
    "U+2122,U+2190-2193,U+2212,U+2215,U+FEFF,U+FFFD"
)


def number(value):
    return f"{value:g}"


def faces():
    tokens = json.loads(TOKENS.read_text(encoding="utf-8"))
    found = set()

    for name, style in tokens["type"].items():
        if name.startswith("$"):
            continue

        family = "mono" if style.get("family") == "mono" else "sans"
        found.add((family, int(style["weight"]), float(style["stretch"].rstrip("%"))))

    return sorted(found)


def source(family):
    _, url, sha256 = SOURCES[family]

    with urllib.request.urlopen(url) as response:
        data = response.read()

    if hashlib.sha256(data).hexdigest() != sha256:
        raise SystemExit(f"{url} is not the font this script was written for.")

    return data


def tabular_digits(font):
    # Points the cmap's digits at the glyphs the tnum feature substitutes for them.
    gsub = font["GSUB"].table
    mapping = {}

    for record in gsub.FeatureList.FeatureRecord:
        if record.FeatureTag == "tnum":
            for index in record.Feature.LookupListIndex:
                for table in gsub.LookupList.Lookup[index].SubTable:
                    mapping.update(getattr(table, "mapping", {}))

    for cmap in font["cmap"].tables:
        for code in range(ord("0"), ord("9") + 1):
            glyph = cmap.cmap.get(code)

            if glyph in mapping:
                cmap.cmap[code] = mapping[glyph]


def rename(font, family):
    names = font["name"]
    postscript = family.replace(" ", "-")

    for record in list(names.names):
        if record.nameID in (1, 2, 3, 4, 6, 16, 17, 25):
            names.removeNames(nameID=record.nameID)

    for name_id, text in ((1, family), (2, "Regular"), (3, postscript), (4, family), (6, postscript), (16, family), (17, "Regular")):
        names.setName(text, name_id, 3, 1, 0x409)


def cut(data, family_key, weight, stretch):
    display, _, _ = SOURCES[family_key]
    font = TTFont(io.BytesIO(data), recalcTimestamp=False)
    face = instancer.instantiateVariableFont(font, {"wght": weight, "wdth": stretch})

    options = subset.Options()
    options.name_IDs = ["*"]
    options.name_languages = ["*"]
    options.notdef_outline = True
    options.layout_features += ["tnum"]
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=subset.parse_unicodes(UNICODES))
    subsetter.subset(face)

    tabular_digits(face)
    family = f"{display} {number(weight)} {number(stretch)}"
    rename(face, family)

    output = io.BytesIO()
    face.save(output)

    return family, output.getvalue()


def main():
    for old in HERE.glob("*.ttf"):
        old.unlink()

    sources = {key: source(key) for key in SOURCES}

    for family_key, weight, stretch in faces():
        family, data = cut(sources[family_key], family_key, weight, stretch)
        path = HERE / (family.replace("Martian Mono", "MartianMono").replace(" ", "-") + ".ttf")
        path.write_bytes(data)
        print(f"{path.name}: {family}, {len(data):,} bytes")


if __name__ == "__main__":
    main()
