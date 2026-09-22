// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { phasesOf } from "./problems";
import { newStep } from "./steps";

describe("phasesOf", () => {
  const partition = newStep("partition", "p");
  const script = newStep("runScript", "s");
  const reboot = newStep("reboot", "r");

  it("takes the server's phase for each step it has seen, wherever the step is now", () => {
    expect(phasesOf([script, partition], [partition, script], ["WindowsPE", "Windows"])).toEqual([
      "Windows",
      "WindowsPE",
    ]);
  });

  it("puts a step the server has not seen in the phase of the step before it", () => {
    const added = newStep("reboot", "n");

    expect(
      phasesOf([partition, script, added], [partition, script], ["WindowsPE", "Windows"]),
    ).toEqual(["WindowsPE", "Windows", "Windows"]);
    expect(phasesOf([reboot, partition], [partition], ["WindowsPE"])).toEqual([
      "WindowsPE",
      "WindowsPE",
    ]);
  });
});
