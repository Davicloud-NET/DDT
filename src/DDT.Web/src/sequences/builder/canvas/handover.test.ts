// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { SequencePhase, SequenceStep } from "../../sequences";
import { newStep } from "../../steps";
import { handover } from "./handover";

function phasesOf(
  steps: readonly SequenceStep[],
  phases: readonly SequencePhase[][],
): Map<string, SequencePhase[]> {
  return new Map(steps.map((step, index) => [step.id, phases[index] ?? []]));
}

describe("the builder's hand-over line", () => {
  it("is drawn above the first step that runs only in the installed Windows", () => {
    const steps = [
      newStep("partition", "p"),
      newStep("applyImage", "i"),
      newStep("runScript", "s"),
      newStep("pause", "a"),
    ];

    expect(
      handover(steps, phasesOf(steps, [["WindowsPE"], ["WindowsPE"], ["Windows"], ["Windows"]])),
    ).toBe("s");
  });

  // As the server lists a stored sequence from before version 4: the script keeps the phase it was stored with.
  it("is not drawn in a Linux sequence whose script was stored for Windows PE", () => {
    const steps = [
      newStep("runScript", "s"),
      newStep("writeRawImage", "w"),
      newStep("writeCloudInitSeed", "c"),
    ];

    expect(handover(steps, phasesOf(steps, [["WindowsPE"], ["LinuxBoot"], ["LinuxBoot"]]))).toBe(
      null,
    );
    expect(handover(steps, phasesOf(steps, [["LinuxBoot"], ["LinuxBoot"], ["LinuxBoot"]]))).toBe(
      null,
    );
  });

  it("is drawn above the first step of a Linux sequence that runs only in the installed Linux", () => {
    const steps = [
      newStep("writeRawImage", "w"),
      newStep("writeCloudInitSeed", "c"),
      newStep("runScript", "s", "Linux"),
      newStep("pause", "a"),
    ];

    expect(
      handover(steps, phasesOf(steps, [["LinuxBoot"], ["LinuxBoot"], ["Linux"], ["Linux"]])),
    ).toBe("s");
    expect(
      handover(steps, phasesOf(steps, [["LinuxBoot"], ["LinuxBoot"], ["LinuxBoot", "Linux"], []])),
    ).toBe(null);
  });

  // The server lists a refused step with the phase it names, which is no hand-over of this sequence.
  it("is not drawn above a step of the other kind", () => {
    const linux = [newStep("writeRawImage", "w"), newStep("runScript", "s")];
    const windows = [newStep("partition", "p"), newStep("runScript", "s", "Linux")];

    expect(handover(linux, phasesOf(linux, [["LinuxBoot"], ["Windows"]]))).toBe(null);
    expect(handover(windows, phasesOf(windows, [["WindowsPE"], ["Linux"]]))).toBe(null);
  });

  it("is not drawn where a step before it may run on either side", () => {
    const steps = [newStep("partition", "p"), newStep("if", "i"), newStep("runScript", "s")];

    expect(
      handover(steps, phasesOf(steps, [["WindowsPE"], ["WindowsPE", "Windows"], ["Windows"]])),
    ).toBe(null);
    expect(handover(steps, phasesOf(steps, [["WindowsPE"], ["WindowsPE"], ["WindowsPE"]]))).toBe(
      null,
    );
  });
});
