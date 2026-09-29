// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { isListedAgain, knownLevel, levelsOf, rowsOf, withNewRow } from "./logLevels";

describe("log levels", () => {
  it("keeps the Default row, and only that one", () => {
    const rows = rowsOf({ Default: "Information", "DDT.Pxe": "Debug" });

    expect(rows.map((row) => row.fixed)).toEqual([true, false]);
  });

  it("leaves out a row whose category is still blank", () => {
    const rows = withNewRow(rowsOf({ Default: "Information" }));

    expect(rows.map((row) => row.id)).toEqual([0, 1]);
    expect(levelsOf(rows)).toEqual({ Default: "Information" });
  });

  it("finds a category listed again further down, whatever its case", () => {
    const rows = rowsOf({ "ddt.pxe": "Debug", Default: "Information", "DDT.Pxe": "Trace" });

    expect(rows.map((_, index) => isListedAgain(rows, index))).toEqual([true, false, false]);
  });

  it("spells a level the way the list does", () => {
    expect(knownLevel("warning")).toBe("Warning");
    expect(knownLevel("Verbose")).toBeUndefined();
  });
});
