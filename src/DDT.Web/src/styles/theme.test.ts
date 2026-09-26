// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

/// <reference types="node" />
// @vitest-environment node

import { readFileSync } from "node:fs";

import { describe, expect, it } from "vitest";

import {
  contrast,
  contrastProblems,
  readTokens,
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

  it("keep every text and control pair readable in both themes", () => {
    expect(contrastProblems(readTokens())).toEqual([]);
  });

  it("measure contrast as WCAG does", () => {
    expect(contrast("#000000", "#FFFFFF")).toBeCloseTo(21, 5);
    expect(contrast("#777777", "#FFFFFF")).toBeCloseTo(4.48, 2);
  });
});
