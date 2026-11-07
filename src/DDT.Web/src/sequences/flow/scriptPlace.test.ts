// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { branch, group, repeat } from "@/test/trees";

import type { RunScriptStep, SequencePhase, SequenceStep } from "../sequences";
import { newStep } from "../steps";
import { insertNode } from "./flowEdits";
import type { Slot } from "./flowTree";
import { scriptPhaseAt } from "./scriptPlace";

const raw = newStep("writeRawImage", "raw");
const seed = newStep("writeCloudInitSeed", "seed");

function script(id: string, phase: SequencePhase): RunScriptStep {
  return { ...(newStep("runScript", id, "Linux") as RunScriptStep), phase };
}

function top(index: number): Slot {
  return { parent: null, body: "steps", index };
}

function into(parent: string, index = 0, body: Slot["body"] = "steps"): Slot {
  return { parent, body, index };
}

// Each case is a place where the server's validator takes a script in that phase and would refuse the other one, or
// where it takes both and the run should go on where the seed carried the agent.
describe("where a new script starts in a Linux sequence", () => {
  it("is the Linux boot environment until a seed step is written before it", () => {
    expect(scriptPhaseAt([], top(0))).toBe("LinuxBoot");
    expect(scriptPhaseAt([raw, seed], top(0))).toBe("LinuxBoot");
    expect(scriptPhaseAt([raw, seed], top(1))).toBe("LinuxBoot");
    expect(scriptPhaseAt([raw], top(1))).toBe("LinuxBoot");
  });

  it("is the installed Linux after the seed, also inside a group or an IF that comes later", () => {
    const steps: SequenceStep[] = [raw, seed, group("g"), branch("i", [], [])];

    expect(scriptPhaseAt([raw, seed], top(2))).toBe("Linux");
    expect(scriptPhaseAt(steps, into("g"))).toBe("Linux");
    expect(scriptPhaseAt(steps, into("i", 0, "then"))).toBe("Linux");
    expect(scriptPhaseAt(steps, into("i", 0, "else"))).toBe("Linux");
    expect(scriptPhaseAt(steps, top(4))).toBe("Linux");
  });

  it("counts a seed inside a group, and inside an IF only when both sides write it", () => {
    const other: SequenceStep = { ...seed, id: "seed2" };

    expect(scriptPhaseAt([raw, group("g", seed)], top(2))).toBe("Linux");
    expect(scriptPhaseAt([raw, group("g", seed)], into("g", 1))).toBe("Linux");
    expect(scriptPhaseAt([raw, branch("i", [seed], [other])], top(2))).toBe("Linux");
    expect(scriptPhaseAt([raw, branch("i", [seed], [])], top(2))).toBe("LinuxBoot");
    expect(scriptPhaseAt([raw, branch("i", [seed], [])], into("i", 1, "then"))).toBe("Linux");
    expect(scriptPhaseAt([raw, branch("i", [seed], [])], into("i", 0, "else"))).toBe("LinuxBoot");
  });

  it("does not count a seed that may be skipped or may fail without ending the run", () => {
    const conditional: SequenceStep = {
      ...seed,
      conditions: [{ variable: "Model", operator: "Equals", value: "Latitude 5440" }],
    };
    const when: SequenceStep = {
      ...seed,
      when: { kind: "test", variable: "Model", operator: "Contains", value: "Latitude" },
    };
    const goesOn: SequenceStep = { ...seed, continueOnError: true };
    const caught: SequenceStep = { ...group("g", seed), continueOnError: true };

    for (const written of [conditional, when, goesOn, caught]) {
      expect(scriptPhaseAt([raw, written], top(2))).toBe("LinuxBoot");
    }

    // Inside the group that goes on after a failure, a failing seed skips the rest of the group.
    expect(scriptPhaseAt([raw, caught], into("g", 1))).toBe("Linux");
  });

  it("stays in the Linux boot environment while a later step still runs there", () => {
    const late = script("late", "LinuxBoot");

    expect(scriptPhaseAt([raw, seed, late], top(2))).toBe("LinuxBoot");
    expect(scriptPhaseAt([raw, seed, group("g"), late], into("g"))).toBe("LinuxBoot");
    expect(scriptPhaseAt([raw, seed, group("g", late)], top(2))).toBe("LinuxBoot");
    expect(scriptPhaseAt([raw, seed, late], top(3))).toBe("Linux");
    // The other side of the same IF is on another path.
    expect(scriptPhaseAt([raw, seed, branch("i", [], [late])], into("i", 0, "then"))).toBe("Linux");
  });

  it("goes on in the installed Linux once a step before it runs there", () => {
    const first = script("first", "Linux");
    const late = script("late", "LinuxBoot");

    expect(scriptPhaseAt([raw, seed, first, late], top(3))).toBe("Linux");
    expect(scriptPhaseAt([raw, seed, group("g", first), repeat("r")], into("r"))).toBe("Linux");
  });

  it("keeps a repeat in the phase it starts in", () => {
    expect(scriptPhaseAt([raw, seed, repeat("r")], into("r"))).toBe("LinuxBoot");
    expect(scriptPhaseAt([raw, seed, repeat("r", group("g"))], into("g"))).toBe("LinuxBoot");
    expect(scriptPhaseAt([raw, seed, script("s", "Linux"), repeat("r")], into("r"))).toBe("Linux");
  });
});

describe("a script added to a Linux sequence", () => {
  it("asks for a restart with 194 in the installed Linux and with nothing in the boot environment", () => {
    expect(insertNode(top(2), "runScript", "Linux", [raw, seed]).nodes[0]).toMatchObject({
      phase: "Linux",
      interpreter: "Shell",
      successExitCodes: [0],
      rebootExitCodes: [194],
    });
    expect(insertNode(top(1), "runScript", "Linux", [raw, seed]).nodes[0]).toMatchObject({
      phase: "LinuxBoot",
      interpreter: "Shell",
      rebootExitCodes: [],
    });
  });

  it("leaves every other step and a Windows sequence's script as they were", () => {
    expect(insertNode(top(2), "pause", "Linux", [raw, seed]).nodes[0]).toMatchObject({
      kind: "pause",
    });
    expect(insertNode(top(0), "runScript", "Windows", []).nodes[0]).toMatchObject({
      phase: "WindowsPE",
      rebootExitCodes: [3010],
    });
  });
});
