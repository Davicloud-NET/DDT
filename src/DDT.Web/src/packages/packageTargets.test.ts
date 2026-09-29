// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { HardwareModelCount } from "@/machines/machines";

import { cleanTargets, manufacturersOf, modelsOf, targetRows } from "./packageTargets";

const models: HardwareModelCount[] = [
  { manufacturer: "Dell Inc.", model: "Latitude 7440", machines: 3 },
  { manufacturer: "Dell Inc.", model: "Latitude 5440", machines: 2 },
  { manufacturer: null, model: "Virtual Machine", machines: 1 },
];

describe("package targets", () => {
  it("edits a target without a manufacturer as an empty one", () => {
    expect(targetRows([{ manufacturer: null, model: "Latitude*" }])).toEqual([
      { key: 0, manufacturer: "", model: "Latitude*" },
    ]);
  });

  it("saves trimmed targets, leaves out rows without a model and sends an empty manufacturer as any", () => {
    expect(
      cleanTargets([
        { key: 0, manufacturer: " Dell Inc. ", model: " Latitude 7440 " },
        { key: 1, manufacturer: "  ", model: "Latitude*" },
        { key: 2, manufacturer: "LENOVO", model: "   " },
      ]),
    ).toEqual([
      { manufacturer: "Dell Inc.", model: "Latitude 7440" },
      { manufacturer: null, model: "Latitude*" },
    ]);
  });

  it("offers each reported manufacturer once", () => {
    expect(manufacturersOf(models)).toEqual(["Dell Inc."]);
  });

  it("offers the models of the manufacturer typed, or every model while it is empty", () => {
    expect(modelsOf(models, " Dell Inc. ").map((model) => model.model)).toEqual([
      "Latitude 7440",
      "Latitude 5440",
    ]);
    expect(modelsOf(models, "")).toHaveLength(3);
  });
});
