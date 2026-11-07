// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { phasesOf } from "@/runs/panel/railSteps";
import type { PathNode } from "@/runs/runPath";

import { insertNode } from "./flow/flowEdits";
import { nodePhasesOf } from "./problems";
import { sequenceEdits } from "./sequenceEdits";
import {
  bootPhase,
  isBootPhase,
  kindOfNodes,
  kindOfPhase,
  scriptInterpreters,
  scriptPhases,
  sequenceKind,
} from "./sequenceKinds";
import type { GroupStep, IfStep, SequenceStep } from "./sequences";
import { interpreterLabel, interpreters, newStep, phaseLabel } from "./steps";

const raw = newStep("writeRawImage", "w");
const seed = newStep("writeCloudInitSeed", "c");
const partition = newStep("partition", "p");

function group(...steps: SequenceStep[]): GroupStep {
  return { ...(newStep("group", "g") as GroupStep), steps };
}

function branch(then: SequenceStep[], otherwise: SequenceStep[]): IfStep {
  return { ...(newStep("if", "i") as IfStep), then, else: otherwise };
}

describe("a sequence's kind", () => {
  it("is Linux when a raw disk image is written anywhere in the tree, like the server's", () => {
    expect(sequenceKind([raw, seed])).toBe("Linux");
    expect(sequenceKind([newStep("pause", "a"), branch([], [group(raw)])])).toBe("Linux");
    expect(sequenceKind([partition, newStep("applyImage", "i")])).toBe("Windows");
    expect(sequenceKind([])).toBe("Windows");

    // A seed or a shell script alone does not make one. The server says what such a sequence lacks.
    expect(sequenceKind([seed, newStep("runScript", "s", "Linux")])).toBe("Windows");
    expect(kindOfNodes([{ kind: "runScript" }, { kind: "writeRawImage" }])).toBe("Linux");
  });

  it("starts in the boot phase of its kind, with that kind's phases and interpreters for a script", () => {
    expect(bootPhase("Windows")).toBe("WindowsPE");
    expect(bootPhase("Linux")).toBe("LinuxBoot");
    expect(kindOfPhase("WindowsPE")).toBe("Windows");
    expect(kindOfPhase("Windows")).toBe("Windows");
    expect(kindOfPhase("LinuxBoot")).toBe("Linux");
    expect((["WindowsPE", "Windows", "LinuxBoot"] as const).filter(isBootPhase)).toEqual([
      "WindowsPE",
      "LinuxBoot",
    ]);
    expect(scriptPhases("Windows")).toEqual(["WindowsPE", "Windows"]);
    expect(scriptPhases("Linux")).toEqual(["LinuxBoot"]);
    expect(scriptInterpreters("Windows")).toEqual(["Cmd", "PowerShell"]);
    expect(scriptInterpreters("Linux")).toEqual(["Shell"]);
    expect([...scriptInterpreters("Windows"), ...scriptInterpreters("Linux")]).toEqual(
      interpreters,
    );
  });

  it("names the Linux boot environment and the shell", () => {
    expect(phaseLabel("LinuxBoot")).toBe("Linux boot environment");
    expect(interpreters.map(interpreterLabel)).toEqual(["cmd", "PowerShell", "Shell"]);
  });
});

describe("a new script", () => {
  it("starts for cmd in Windows PE, and for the shell in the Linux boot environment without restart codes", () => {
    expect(newStep("runScript", "s")).toMatchObject({
      phase: "WindowsPE",
      interpreter: "Cmd",
      rebootExitCodes: [3010],
    });
    expect(newStep("runScript", "s", "Linux")).toMatchObject({
      phase: "LinuxBoot",
      interpreter: "Shell",
      successExitCodes: [0],
      rebootExitCodes: [],
    });
  });

  it("takes the kind of the sequence it is added to", () => {
    const slot = { parent: null, body: "steps" as const, index: 0 };

    expect(insertNode(slot, "runScript", "Linux").nodes[0]).toMatchObject({ phase: "LinuxBoot" });
    expect(insertNode(slot, "runScript").nodes[0]).toMatchObject({ phase: "WindowsPE" });

    const draft = { name: "Linux", description: "", steps: [raw], variables: [], inputs: [] };
    const added = sequenceEdits(draft, { type: "addStep", kind: "runScript", id: "s" });

    expect(added.steps[1]).toMatchObject({ phase: "LinuxBoot", interpreter: "Shell" });
  });
});

describe("a node without a phase of its own yet", () => {
  it("shows the boot phase of the sequence's kind in the builder", () => {
    const saved = { steps: [] as SequenceStep[], stepPhases: [], nodePhases: null };

    expect(nodePhasesOf([newStep("pause", "a"), raw], saved).get("a")).toEqual(["LinuxBoot"]);
    expect(nodePhasesOf([newStep("pause", "a"), partition], saved).get("a")).toEqual(["WindowsPE"]);
  });

  it("shows the boot phase of the run's kind on the rail", () => {
    const leaves = [{ step: null }, { step: null }] as unknown as PathNode[];

    expect(phasesOf(leaves, "LinuxBoot").map((leaf) => leaf.phase)).toEqual([
      "LinuxBoot",
      "LinuxBoot",
    ]);
    expect(phasesOf(leaves).map((leaf) => leaf.phase)).toEqual(["WindowsPE", "WindowsPE"]);
  });
});
