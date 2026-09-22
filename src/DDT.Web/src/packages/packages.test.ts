// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { HardwareModelCount } from "@/machines/machines";

import { matchesModel, matchingMachines } from "./packages";

const models: HardwareModelCount[] = [
  { manufacturer: "Dell Inc.", model: "Latitude 7440", machines: 3 },
  { manufacturer: "Dell Inc.", model: "Latitude 5440", machines: 2 },
  { manufacturer: "LENOVO", model: "20XW0055GE", machines: 1 },
];

describe("matchesModel", () => {
  it("compares as the server does: without case, with runs of spaces as one", () => {
    expect(matchesModel("latitude  7440 ", "Latitude 7440")).toBe(true);
    expect(matchesModel("Latitude 7440", "Latitude 5440")).toBe(false);
    expect(matchesModel(null, "anything")).toBe(true);
    expect(matchesModel("Latitude", null)).toBe(false);
  });

  it("matches every model that starts with the text before a trailing star", () => {
    expect(matchesModel("Latitude*", "Latitude 7440")).toBe(true);
    expect(matchesModel("20XW*", "20xw0055ge")).toBe(true);
    expect(matchesModel("Latitude 7*", "Latitude 5440")).toBe(false);
  });
});

describe("matchingMachines", () => {
  it("counts the machines of every model a target names once", () => {
    expect(
      matchingMachines(
        [
          { manufacturer: "Dell Inc.", model: "Latitude*" },
          { manufacturer: null, model: "Latitude 7440" },
        ],
        models,
      ),
    ).toBe(5);
    expect(matchingMachines([{ manufacturer: "HP", model: "Latitude 7440" }], models)).toBe(0);
    expect(matchingMachines([], models)).toBe(0);
  });
});
