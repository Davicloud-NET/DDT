// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { DeploymentStepView, RunActivity, RunPauseView } from "@/deployments/deployments";
import { walk, type TreeEntry } from "@/sequences/flow/flowTree";
import type { IfBranch, SequenceDefinition, SequenceStep } from "@/sequences/sequences";
import { isContainer } from "@/sequences/steps";

// The path a run took through its tree. It's built from the tree the run was given and the latest visit of each node
// that the server keeps. A node in a branch an IF didn't take, or in a skipped container, is not taken. An IF that
// hasn't decided yet keeps both branches ahead.

export type PathState =
  "done" | "running" | "failed" | "skipped" | "paused" | "waiting" | "notTaken";

export interface PathNode {
  node: SequenceStep;
  // Where the node sits in the tree, and its leaf number. The number is null for a container.
  entry: TreeEntry;
  // The run's step for the node. Null if the server has none, for example for a node it hasn't reported yet.
  step: DeploymentStepView | null;
  state: PathState;
  // The containers around the node, outermost first, with the branch of an IF the node sits in.
  ancestors: { node: SequenceStep; branch: "then" | "else" | null }[];
}

// What a wire of the flow connects, as the flow's layout names it. from is the node before the wire and to the node
// after it. Either is null at a port, a join or a frame's edge. branch is set on the wires of an IF's branch that no
// node in the branch decides.
export interface PathRoute {
  from: string | null;
  to: string | null;
  branch: { id: string; name: "then" | "else" } | null;
}

// Taken: the run went along it. Ahead: the run may still go along it. Not: the run did not take it.
export type PathTone = "taken" | "ahead" | "not";

export interface RunPath {
  // Every node of the tree, in pre-order.
  nodes: PathNode[];
  byId: ReadonlyMap<string, PathNode>;
  // The leaves on the path, in order: those done, running or paused, and those still ahead.
  leaves: PathNode[];
  // The node the run is at: the paused or running one, the innermost if containers run around it. Otherwise it's the
  // leaf that failed. Null if the run is at no node.
  current: PathNode | null;
  // Whether the tree has containers, so that its path can differ from its list of steps.
  isTree: boolean;
  tone: (route: PathRoute) => PathTone;
}

// What the path needs of the run besides its steps: what the agent does, and the pause it waits at.
export interface PathRun {
  activity?: RunActivity | null;
  pause?: RunPauseView | null;
}

const finished: ReadonlySet<PathState> = new Set(["done", "skipped", "failed"]);

function branchName(branch: IfBranch | null | undefined): "then" | "else" | null {
  return branch === "Then" ? "then" : branch === "Else" ? "else" : null;
}

function stateOf(step: DeploymentStepView | null, node: SequenceStep, run: PathRun): PathState {
  if (step === null) {
    return "waiting";
  }

  switch (step.state) {
    case "Pending":
      return "waiting";
    case "Running": {
      // If the page knows what the agent is doing now, that decides whether the run is paused. The pause the run was
      // read with names the step and its visit.
      const pause = run.pause ?? null;
      const holds =
        run.activity === undefined || run.activity === null
          ? pause !== null
          : run.activity === "Paused";
      const here =
        node.kind === "pause" ||
        (pause !== null &&
          pause.stepId === step.stepId &&
          pause.pass === (step.pass ?? pause.pass));

      return holds && here ? "paused" : "running";
    }
    case "Done":
      return "done";
    case "Failed":
      return "failed";
    case "Skipped":
      return "skipped";
  }
}

// If the page doesn't have the run's definition, the run's steps are treated as a flat list.
function stepsAsTree(steps: readonly DeploymentStepView[]): SequenceStep[] {
  return [...steps]
    .sort((a, b) => a.index - b.index)
    .map((step) => {
      const common = {
        id: step.stepId,
        name: step.name,
        conditions: [],
        continueOnError: false,
        rebootAfter: false,
      };

      switch (step.kind) {
        case "group":
          return { ...common, kind: "group", steps: [] };
        case "if":
          return {
            ...common,
            kind: "if",
            test: { kind: "all", parts: [] },
            then: [],
            else: [],
          };
        case "repeat":
          return {
            ...common,
            kind: "repeat",
            steps: [],
            until: { kind: "all", parts: [] },
            maxTimes: 1,
            goOnAtLimit: false,
          };
        default:
          // It's a leaf, so only its kind and name are read.
          return { ...common, kind: step.kind } as unknown as SequenceStep;
      }
    });
}

export function runPath(
  definition: SequenceDefinition | null,
  steps: readonly DeploymentStepView[],
  run: PathRun = {},
): RunPath {
  const tree =
    definition !== null && definition.steps.length > 0 ? definition.steps : stepsAsTree(steps);
  const entries = walk(tree);
  const stepsById = new Map(steps.map((step) => [step.stepId, step]));
  const byId = new Map<string, PathNode>();
  const nodes: PathNode[] = [];

  for (const entry of entries) {
    const parent = entry.parent === null ? null : (byId.get(entry.parent) ?? null);
    const step = stepsById.get(entry.node.id) ?? null;
    const branch = entry.body === "then" || entry.body === "else" ? entry.body : null;
    const ancestors = parent === null ? [] : [...parent.ancestors, { node: parent.node, branch }];
    // A node is not taken if its container wasn't: an IF took the other branch, the container was skipped, or the
    // container itself wasn't taken.
    const taken = branchName(parent?.step?.branch);
    const excluded =
      parent !== null &&
      (parent.state === "notTaken" ||
        parent.state === "skipped" ||
        (branch !== null && taken !== null && taken !== branch));
    const node: PathNode = {
      node: entry.node,
      entry,
      step,
      state: excluded ? "notTaken" : stateOf(step, entry.node, run),
      ancestors,
    };

    nodes.push(node);

    if (!byId.has(entry.node.id)) {
      byId.set(entry.node.id, node);
    }
  }

  const leaves = nodes.filter((node) => !isContainer(node.node) && node.state !== "notTaken");
  const active = nodes.filter((node) => node.state === "paused" || node.state === "running");
  const current =
    active.find((node) => node.state === "paused") ??
    active.at(-1) ??
    leaves.find((node) => node.state === "failed") ??
    null;

  const stateAt = (id: string | null): PathState | null =>
    id === null ? null : (byId.get(id)?.state ?? null);
  const reached = (id: string | null) => {
    const state = stateAt(id);

    return state !== null && state !== "waiting" && state !== "notTaken";
  };
  const passed = (id: string | null) => {
    const state = stateAt(id);

    return state !== null && finished.has(state);
  };

  const tone = (route: PathRoute): PathTone => {
    const from = stateAt(route.from);
    const to = stateAt(route.to);

    if (from === "notTaken" || to === "notTaken") {
      return "not";
    }

    if (route.branch !== null) {
      const decider = byId.get(route.branch.id) ?? null;
      const took = branchName(decider?.step?.branch);

      if (
        decider === null ||
        decider.state === "notTaken" ||
        decider.state === "skipped" ||
        (took !== null && took !== route.branch.name)
      ) {
        return decider === null ? "ahead" : "not";
      }

      // A port, or a branch without nodes: taken once the IF went that way.
      if (route.to === null && (route.from === null || route.from === route.branch.id)) {
        return took === route.branch.name ? "taken" : "ahead";
      }
    }

    // A repeat's wire back: taken once the run went round again.
    if (route.from !== null && route.from === route.to) {
      return (byId.get(route.from)?.step?.iteration ?? 0) > 1 ? "taken" : "ahead";
    }

    if (route.to !== null) {
      return reached(route.to) ? "taken" : "ahead";
    }

    return passed(route.from) ? "taken" : "ahead";
  };

  return {
    nodes,
    byId,
    leaves,
    current,
    isTree: entries.some((entry) => isContainer(entry.node)),
    tone,
  };
}

// Numbers a run's steps the way the flow and the rail do. Leaves count from 1 in tree order, which is the order of the
// steps. Containers get no number. Without containers, a step's number is its position.
export function leafNumbers(steps: readonly DeploymentStepView[]): Map<string, number | null> {
  const containers = steps
    .filter((step) => step.kind === "group" || step.kind === "if" || step.kind === "repeat")
    .map((step) => step.index);

  return new Map(
    steps.map((step) => [
      step.stepId,
      containers.includes(step.index)
        ? null
        : step.index + 1 - containers.filter((index) => index < step.index).length,
    ]),
  );
}

// How far the path is, from 0 to 100: each leaf on it that finished counts whole, the running one by its
// percentage. Steps take very different times, so this is a rough measure, shown as such.
export function pathPercent(path: RunPath): number {
  if (path.leaves.length === 0) {
    return 0;
  }

  const done = path.leaves.reduce(
    (sum, leaf) =>
      sum +
      (leaf.state === "running"
        ? Math.min(100, Math.max(0, leaf.step?.percent ?? 0)) / 100
        : finished.has(leaf.state)
          ? 1
          : 0),
    0,
  );

  return Math.round((done / path.leaves.length) * 100);
}

// How many leaves of the path the run has reached: finished, running or paused.
export function reachedCount(path: RunPath): number {
  return path.leaves.filter((leaf) => leaf.state !== "waiting").length;
}
