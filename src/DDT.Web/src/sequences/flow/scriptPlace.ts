// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequencePhase, SequenceStep } from "../sequences";
import { indexTree, listAt, walk, type Slot, type TreeEntry } from "./flowTree";

// The nodes a run may pass before and after a slot: at each level around it, the siblings on either side with all
// they hold. An IF's other branch is on neither side.
interface Around {
  before: SequenceStep[];
  after: SequenceStep[];
  inRepeat: boolean;
}

function around(steps: readonly SequenceStep[], slot: Slot): Around {
  const index = indexTree(steps);
  const found: Around = { before: [], after: [], inRepeat: false };
  let at: Slot | undefined = slot;
  let skip = 0;

  // The slot's own list first, then the list each container around it sits in, without that container.
  while (at !== undefined) {
    const list = listAt([...steps], at.parent, at.body) ?? [];
    const parent: TreeEntry | undefined =
      at.parent === null ? undefined : index.byId.get(at.parent);

    found.before.push(...list.slice(0, at.index));
    found.after.push(...list.slice(at.index + skip));
    found.inRepeat ||= parent?.node.kind === "repeat";
    at =
      parent === undefined
        ? undefined
        : { parent: parent.parent, body: parent.body, index: parent.index };
    skip = 1;
  }

  return found;
}

// Whether the node writes the seed whenever a run passes it, as the server's validator asks for a step in the
// installed Linux: no conditions and no going on after a failure, and in an IF on both sides.
function writesSeed(node: SequenceStep): boolean {
  if (node.conditions.length > 0 || (node.when ?? null) !== null || node.continueOnError) {
    return false;
  }

  switch (node.kind) {
    case "writeCloudInitSeed":
      return true;
    case "group":
      return node.steps.some(writesSeed);
    case "if":
      return node.then.some(writesSeed) && node.else.some(writesSeed);
    default:
      return false;
  }
}

function inside(nodes: readonly SequenceStep[]): SequenceStep[] {
  return walk(nodes).map((entry) => entry.node);
}

// The disk image and the seed are written in the Linux boot environment. A script says where it runs.
function asksFor(node: SequenceStep, phase: SequencePhase): boolean {
  if (node.kind === "runScript") {
    return node.phase === phase;
  }

  return (
    phase === "LinuxBoot" && (node.kind === "writeRawImage" || node.kind === "writeCloudInitSeed")
  );
}

// Where a new script starts in a Linux sequence, so that the server's validator has nothing to say about its place:
// in the installed Linux once a seed step runs before the slot on every path, and in the Linux boot environment
// otherwise. A repeat stays in one phase, and no step of the boot environment may come after the script.
export function scriptPhaseAt(steps: readonly SequenceStep[], slot: Slot): SequencePhase {
  const { before, after, inRepeat } = around(steps, slot);

  if (inside(before).some((node) => asksFor(node, "Linux"))) {
    return "Linux";
  }

  if (inRepeat || inside(after).some((node) => asksFor(node, "LinuxBoot"))) {
    return "LinuxBoot";
  }

  return before.some(writesSeed) ? "Linux" : "LinuxBoot";
}
