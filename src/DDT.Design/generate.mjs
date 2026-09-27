// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Turns tokens.json into the web theme and the resources of the console in Windows PE. Run it from src/DDT.Web with
// `npm run tokens`; a test on each side fails while the written file differs from what this renders, so the tokens
// stay the one source.

import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const here = (path) => fileURLToPath(new URL(path, import.meta.url));

export const tokensPath = here("./tokens.json");
export const webThemePath = here("../DDT.Web/src/styles/theme.css");
export const consoleThemePath = here("../DDT.MachineConsole/Theme/Tokens.axaml");

const licence = [
  "Copyright (C) 2026 Davicloud",
  "SPDX-License-Identifier: GPL-3.0-or-later",
  "Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.",
];

const written =
  "Written by src/DDT.Design/generate.mjs from src/DDT.Design/tokens.json. Change those and run npm run tokens.";

const header = [...licence.map((line) => `/* ${line} */`), "", `/* ${written} */`];

// The console's fonts, which src/DDT.MachineConsole/Assets/Fonts/cut_fonts.py cuts: one static face per weight and
// width a type uses, named after both.
const consoleFonts = "avares://ddt-console/Assets/Fonts";
const consoleFamilies = { sans: "Archivo", mono: "Martian Mono" };

export function readTokens() {
  return JSON.parse(readFileSync(tokensPath, "utf8"));
}

function entries(object) {
  return Object.entries(object).filter(([key]) => !key.startsWith("$"));
}

function fontStack(families) {
  return families.map((family) => (/[\s-]/.test(family) && !family.includes("(") ? `"${family}"` : family)).join(", ");
}

// The relative luminance and contrast ratio of WCAG 2.2, for opaque #RRGGBB colours.
function luminance(hex) {
  const channels = [1, 3, 5].map((start) => parseInt(hex.slice(start, start + 2), 16) / 255);
  const [r, g, b] = channels.map((c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));

  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrast(foreground, background) {
  const [light, dark] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);

  return (light + 0.05) / (dark + 0.05);
}

// Every pair tokens.json lists, in both themes, that falls below its minimum.
export function contrastProblems(tokens) {
  const problems = [];

  for (const [theme, colours] of entries(tokens.color)) {
    const resolve = (name) => (name === "white" ? "#FFFFFF" : colours[name]);

    for (const [foreground, background, minimum] of tokens.contrast.pairs) {
      const [fg, bg] = [resolve(foreground), resolve(background)];

      if (!/^#[0-9A-Fa-f]{6}$/.test(fg ?? "") || !/^#[0-9A-Fa-f]{6}$/.test(bg ?? "")) {
        problems.push(`${theme}: ${foreground} on ${background} needs two opaque #RRGGBB colours`);
        continue;
      }

      const ratio = contrast(fg, bg);

      if (ratio < minimum) {
        problems.push(`${theme}: ${foreground} on ${background} is ${ratio.toFixed(2)}:1, below ${minimum}:1`);
      }
    }
  }

  return problems;
}

function typeUtility(name, type) {
  const lines = [
    `@utility type-${name} {`,
    ...(type.family ? [`  font-family: var(--font-${type.family});`] : []),
    `  font-size: ${type.size};`,
    `  line-height: ${type.lineHeight};`,
    `  font-weight: ${type.weight};`,
    `  font-stretch: ${type.stretch};`,
    ...(type.letterSpacing ? [`  letter-spacing: ${type.letterSpacing};`] : []),
    ...(type.transform ? [`  text-transform: ${type.transform};`] : []),
    "}",
  ];

  return lines.join("\n");
}

export function renderWebTheme(tokens) {
  const light = entries(tokens.color.light);
  const dark = entries(tokens.color.dark);
  const names = light.map(([name]) => name);
  const missing = names.filter((name) => !(name in tokens.color.dark));

  if (missing.length > 0) {
    throw new Error(`The dark theme has no value for ${missing.join(", ")}.`);
  }

  const variables = (colours) => colours.map(([name, value]) => `  --sg-${name}: ${value};`);

  return [
    ...header,
    "",
    ":root {",
    "  color-scheme: light;",
    ...variables(light),
    "}",
    "",
    ":root.dark {",
    "  color-scheme: dark;",
    ...variables(dark),
    "}",
    "",
    "/* Only the tokens exist: Tailwind's own palette, fonts, sizes, radii and shadows are cleared. */",
    "@theme {",
    "  --color-*: initial;",
    "  --font-*: initial;",
    "  --text-*: initial;",
    "  --radius-*: initial;",
    "  --shadow-*: initial;",
    "  --inset-shadow-*: initial;",
    "  --drop-shadow-*: initial;",
    "}",
    "",
    "@theme static {",
    "  --color-white: #FFFFFF;",
    ...names.map((name) => `  --color-${name}: var(--sg-${name});`),
    "",
    `  --font-sans: ${fontStack(tokens.font.sans)};`,
    `  --font-mono: ${fontStack(tokens.font.mono)};`,
    "",
    ...entries(tokens.type).map(([name, type]) => `  --text-${name}: ${type.size};\n  --text-${name}--line-height: ${type.lineHeight};`),
    "",
    ...entries(tokens.radius).map(([name, value]) => `  --radius-${name}: ${value};`),
    "",
    "  --shadow-panel: 0 0 0 1px var(--sg-edge-panel);",
    "  --shadow-overlay: 0 0 0 1px var(--sg-edge-overlay), 0 14px 36px -8px var(--sg-shadow-overlay);",
    "",
    `  --ease-standard: ${tokens.motion.easing};`,
    `  --default-transition-duration: ${tokens.motion.fast};`,
    `  --default-transition-timing-function: ${tokens.motion.easing};`,
    "}",
    "",
    ...entries(tokens.type).map(([name, type]) => typeUtility(name, type)).flatMap((block) => [block, ""]),
  ].join("\n");
}

// "ink-2" becomes Ink2, the way the console's resource keys name a token.
function pascal(name) {
  return name
    .split("-")
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join("");
}

// #RRGGBB, or rgb(r g b / alpha), as Avalonia's #AARRGGBB.
function avaloniaColor(value) {
  const hex = /^#([0-9A-Fa-f]{6})$/.exec(value);

  if (hex) {
    return `#FF${hex[1].toUpperCase()}`;
  }

  const rgb = /^rgb\((\d+) (\d+) (\d+) \/ ([0-9.]+)\)$/.exec(value);

  if (!rgb) {
    throw new Error(`${value} is neither #RRGGBB nor rgb(r g b / alpha).`);
  }

  const byte = (number) => Math.round(number).toString(16).padStart(2, "0").toUpperCase();
  const [red, green, blue] = rgb.slice(1, 4).map(Number);

  return `#${byte(Number(rgb[4]) * 255)}${byte(red)}${byte(green)}${byte(blue)}`;
}

function pixels(value) {
  const match = /^(-?[0-9.]+)px$/.exec(value);

  if (!match) {
    throw new Error(`${value} is not a length in px.`);
  }

  return Number(match[1]);
}

// At most three decimals, without trailing zeros.
function decimal(number) {
  return String(Math.round(number * 1000) / 1000);
}

// The face a type is set in, by its family, weight and width, as cut_fonts.py names the faces.
export function consoleFontFamily(type) {
  const family = consoleFamilies[type.family ?? "sans"];

  return `${consoleFonts}#${family} ${String(type.weight)} ${decimal(parseFloat(type.stretch))}`;
}

// The console's ResourceDictionary: every colour as a Color and a brush for the light and the dark theme, the radii,
// and each type's face, size, line height and letter spacing in pixels.
export function renderConsoleTheme(tokens) {
  const missing = entries(tokens.color.light)
    .map(([name]) => name)
    .filter((name) => !(name in tokens.color.dark));

  if (missing.length > 0) {
    throw new Error(`The dark theme has no value for ${missing.join(", ")}.`);
  }

  // The overlay's shadow is the web's shadow-overlay: its edge is drawn as a border, its drop as a BoxShadow.
  const colours = (theme) => [
    ...entries(tokens.color[theme]).flatMap(([name, value]) => [
      `      <Color x:Key="Sg${pascal(name)}Color">${avaloniaColor(value)}</Color>`,
      `      <SolidColorBrush x:Key="Sg${pascal(name)}" Color="${avaloniaColor(value)}" />`,
    ]),
    `      <BoxShadows x:Key="SgOverlayShadow">0 14 36 -8 ${avaloniaColor(tokens.color[theme]["shadow-overlay"])}</BoxShadows>`,
  ];

  const type = entries(tokens.type).flatMap(([name, style]) => {
    const size = pixels(style.size);
    const key = `SgType${pascal(name)}`;
    const letterSpacing = style.letterSpacing ? parseFloat(style.letterSpacing) * size : 0;

    return [
      `  <FontFamily x:Key="${key}Family">${consoleFontFamily(style)}</FontFamily>`,
      `  <x:Double x:Key="${key}Size">${decimal(size)}</x:Double>`,
      `  <x:Double x:Key="${key}LineHeight">${decimal(size * Number(style.lineHeight))}</x:Double>`,
      `  <x:Double x:Key="${key}LetterSpacing">${decimal(letterSpacing)}</x:Double>`,
    ];
  });

  return [
    ...licence.map((line) => `<!-- ${line} -->`),
    "",
    `<!-- ${written} -->`,
    '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
    '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
    "  <ResourceDictionary.ThemeDictionaries>",
    '    <ResourceDictionary x:Key="Light">',
    ...colours("light"),
    "    </ResourceDictionary>",
    '    <ResourceDictionary x:Key="Dark">',
    ...colours("dark"),
    "    </ResourceDictionary>",
    "  </ResourceDictionary.ThemeDictionaries>",
    "",
    ...entries(tokens.radius).map(
      ([name, value]) => `  <CornerRadius x:Key="SgRadius${pascal(name)}">${decimal(pixels(value))}</CornerRadius>`,
    ),
    "",
    "  <!-- Each type has a face of its own, so no weight or width is synthesized. The console sets tags in capitals. -->",
    ...type,
    "</ResourceDictionary>",
    "",
  ].join("\n");
}

function main() {
  const tokens = readTokens();
  const problems = contrastProblems(tokens);

  if (problems.length > 0) {
    console.error(`Contrast too low:\n${problems.map((problem) => `  ${problem}`).join("\n")}`);
    process.exit(1);
  }

  writeFileSync(webThemePath, renderWebTheme(tokens), "utf8");
  console.log(`Wrote ${webThemePath}`);
  writeFileSync(consoleThemePath, renderConsoleTheme(tokens), "utf8");
  console.log(`Wrote ${consoleThemePath}`);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main();
}
