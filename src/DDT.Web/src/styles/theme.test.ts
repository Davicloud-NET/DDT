// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

/// <reference types="node" />
// @vitest-environment node

import { readFileSync } from "node:fs";

import { describe, expect, it } from "vitest";

import {
  consoleFontFamily,
  consoleThemePath,
  contrast,
  contrastProblems,
  readTokens,
  renderConsoleTheme,
  renderWebTheme,
  webThemePath,
} from "../../../DDT.Design/generate.mjs";

describe("the design tokens", () => {
  it("are what theme.css was written from", () => {
    const written = readFileSync(webThemePath, "utf8").replace(/\r\n/g, "\n");

    expect(written, "src/DDT.Design/tokens.json changed; run npm run tokens").toBe(
      renderWebTheme(readTokens()),
    );
  });

  // The console in Windows PE has its own test of the same file, from the .NET side.
  it("are what the console's Tokens.axaml was written from", () => {
    const written = readFileSync(consoleThemePath, "utf8").replace(/\r\n/g, "\n");

    expect(written, "src/DDT.Design/tokens.json changed; run npm run tokens").toBe(
      renderConsoleTheme(readTokens()),
    );
  });

  it("name the console's face for a type by its weight and width", () => {
    expect(consoleFontFamily({ weight: 750, stretch: "62%" })).toBe(
      "avares://ddt-console/Assets/Fonts#Archivo 750 62",
    );
    expect(consoleFontFamily({ family: "mono", weight: 400, stretch: "87.5%" })).toBe(
      "avares://ddt-console/Assets/Fonts#Martian Mono 400 87.5",
    );
  });

  it("keep every text and control pair readable in both themes", () => {
    expect(contrastProblems(readTokens())).toEqual([]);
  });

  it("measure contrast as WCAG does", () => {
    expect(contrast("#000000", "#FFFFFF")).toBeCloseTo(21, 5);
    expect(contrast("#777777", "#FFFFFF")).toBeCloseTo(4.48, 2);
  });
});
