// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { DeploymentStepView } from "@/deployments/deployments";
import { layoutFlow } from "@/sequences/flow/flowLayout";
import { stepView } from "@/test/builders";
import { branch, leaf, repeat } from "@/test/trees";
import { node, treeDefinition, treeSteps } from "@/test/treeRun";

import { leafNumbers, pathPercent, reachedCount, runPath } from "./runPath";

function states(path: ReturnType<typeof runPath>): Record<string, string> {
  return Object.fromEntries(path.nodes.map((each) => [each.node.name, each.state]));
}

describe("runPath", () => {
  it("follows the branch an IF took and leaves the other out of the path", () => {
    const path = runPath(treeDefinition, treeSteps, { activity: "Paused" });

    expect(states(path)).toEqual({
      "Partition the disk": "done",
      "Is it a Latitude?": "done",
      "Apply Windows 11 for Latitudes": "done",
      "Add the Latitude drivers": "done",
      "Apply Windows 11": "notTaken",
      "Name the computer": "done",
      "Write the answer file": "done",
      "Join the domain": "done",
      "Berlin office": "done",
      "Map the site share": "done",
      "Install the site printer": "skipped",
      "Wait for the share": "done",
      "Test the share": "done",
      "Check the asset tag": "paused",
      Restart: "waiting",
    });
    expect(path.leaves.map((each) => each.entry.number)).toEqual([
      1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12,
    ]);
    expect(path.current?.node.id).toBe(node.pause);
    expect(reachedCount(path)).toBe(10);
    expect(path.isTree).toBe(true);
  });

  it("draws the path taken in ink, the branch not taken as not taken, and what is ahead as ahead", () => {
    const path = runPath(treeDefinition, treeSteps, { activity: "Paused" });
    const layout = layoutFlow(treeDefinition.steps);
    const toneOf = (from: string | null, to: string | null) =>
      layout.wires
        .filter((wire) => wire.from === from && wire.to === to)
        .map((wire) => path.tone(wire));

    // Into the Then branch and out of it, and the Else branch's own wires.
    expect(toneOf(node.latitude, node.applyLatitude)).toEqual(["taken"]);
    expect(toneOf(node.latitude, node.applyOther)).toEqual(["not"]);
    expect(toneOf(node.applyOther, null)).toEqual(["not", "not"]);
    expect(toneOf(node.drivers, null)).toEqual(["taken"]);
    // The repeat went round again; the pause holds the run, so the wire to the restart is ahead.
    expect(toneOf(node.wait, node.wait)).toEqual(["taken"]);
    expect(toneOf(node.wait, node.pause)).toEqual(["taken"]);
    expect(toneOf(node.pause, node.restart)).toEqual(["ahead"]);
    // The ports: Then in ink, Else not taken.
    expect(
      path.tone({ from: node.latitude, to: null, branch: { id: node.latitude, name: "then" } }),
    ).toBe("taken");
    expect(
      path.tone({ from: node.latitude, to: null, branch: { id: node.latitude, name: "else" } }),
    ).toBe("not");
  });

  it("keeps both branches of an IF that has not decided yet ahead", () => {
    const definition = {
      version: 3,
      steps: [leaf("a"), branch("b", [leaf("c")], [leaf("d")]), leaf("e")],
    };
    const steps: DeploymentStepView[] = [
      stepView({ stepId: "a", index: 0, state: "Running", percent: 50, pass: 1 }),
      stepView({ stepId: "b", index: 1, kind: "if" }),
      stepView({ stepId: "c", index: 2 }),
      stepView({ stepId: "d", index: 3 }),
      stepView({ stepId: "e", index: 4 }),
    ];
    const path = runPath(definition, steps);

    expect(path.leaves.map((each) => each.node.id)).toEqual(["a", "c", "d", "e"]);
    expect(path.tone({ from: "b", to: "c", branch: { id: "b", name: "then" } })).toBe("ahead");
    expect(path.tone({ from: "b", to: "d", branch: { id: "b", name: "else" } })).toBe("ahead");
    expect(path.current?.node.id).toBe("a");
    expect(pathPercent(path)).toBe(13);
  });

  it("leaves out what is inside a container that was skipped", () => {
    const definition = { version: 3, steps: [repeat("r", leaf("x")), leaf("y")] };
    const steps = [
      stepView({ stepId: "r", index: 0, kind: "repeat", state: "Skipped", pass: 1 }),
      stepView({ stepId: "x", index: 1, state: "Skipped", pass: 1 }),
      stepView({ stepId: "y", index: 2, state: "Failed", pass: 1, error: "Exit code 1" }),
    ];
    const path = runPath(definition, steps);

    expect(states(path)).toEqual({
      "Repeat r": "skipped",
      "Step x": "notTaken",
      "Step y": "failed",
    });
    expect(path.current?.node.id).toBe("y");
  });

  it("is not paused once the agent went on, whatever the pause the run was read with", () => {
    const path = runPath(treeDefinition, treeSteps, { activity: "Step" });

    expect(path.byId.get(node.pause)?.state).toBe("running");
  });

  it("reads a run without its definition as the list of its steps", () => {
    const path = runPath(null, [
      stepView({ stepId: "one", index: 0, state: "Done" }),
      stepView({ stepId: "two", index: 1, state: "Running" }),
    ]);

    expect(path.leaves.map((each) => [each.node.id, each.state, each.entry.number])).toEqual([
      ["one", "done", 1],
      ["two", "running", 2],
    ]);
    expect(path.isTree).toBe(false);
  });
});

describe("leafNumbers", () => {
  it("numbers the leaves as the flow does and gives containers none", () => {
    const numbers = leafNumbers(treeSteps);

    expect(numbers.get(node.partition)).toBe(1);
    expect(numbers.get(node.latitude)).toBeNull();
    expect(numbers.get(node.applyOther)).toBe(4);
    expect(numbers.get(node.restart)).toBe(12);
  });
});
