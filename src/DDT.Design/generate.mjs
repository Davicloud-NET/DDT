// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Turns tokens.json into the web theme. Run it from src/DDT.Web with `npm run tokens`; a test fails while the
// written theme differs from what this renders, so the tokens stay the one source.

import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const here = (path) => fileURLToPath(new URL(path, import.meta.url));

export const tokensPath = here("./tokens.json");
export const webThemePath = here("../DDT.Web/src/styles/theme.css");

const header = [
  "/* Copyright (C) 2026 Davicloud */",
  "/* SPDX-License-Identifier: GPL-3.0-or-later */",
  "/* Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE. */",
  "",
  "/* Written by src/DDT.Design/generate.mjs from src/DDT.Design/tokens.json. Change those and run npm run tokens. */",
];

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

function main() {
  const tokens = readTokens();
  const problems = contrastProblems(tokens);

  if (problems.length > 0) {
    console.error(`Contrast too low:\n${problems.map((problem) => `  ${problem}`).join("\n")}`);
    process.exit(1);
  }

  writeFileSync(webThemePath, renderWebTheme(tokens), "utf8");
  console.log(`Wrote ${webThemePath}`);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main();
}
