// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceStep } from "@/sequences/sequences";
import { newStep } from "@/sequences/steps";
import type { FlowNodeState } from "@/ui/FlowNode";

// A node of the design page's flow, with what it shows in the editor and on a run.
export interface DemoNode {
  kind: SequenceStep["kind"];
  name: string;
  detail: string;
  run: FlowNodeState;
  runDetail?: string;
  code?: boolean;
  problem?: boolean;
  inside?: DemoNode[];
  otherwise?: DemoNode[];
}

// Every kind of node, an IF, a group and a repeat, laid out by the flow builder's layout.
export const demoFlow: DemoNode[] = [
  {
    kind: "partition",
    name: "Partition the disk",
    detail: "Erases the disk and makes the partitions",
    run: "done",
    runDetail: "Took 48 s",
  },
  {
    kind: "if",
    name: "Is it a Latitude?",
    detail: 'Model contains "Latitude"',
    run: "done",
    runDetail: 'Took Then: Latitude 7450 contains "Latitude"',
    inside: [
      {
        kind: "applyImage",
        name: "Apply Windows 11 for Latitudes",
        detail: "Windows 11 25H2 Enterprise, with Office",
        run: "done",
        runDetail: "Took 3 min 10 s",
      },
      {
        kind: "injectDrivers",
        name: "Add the Latitude drivers",
        detail: "Driver packages matched to the model",
        run: "done",
        runDetail: "Took 41 s",
      },
    ],
    otherwise: [
      {
        kind: "applyImage",
        name: "Apply Windows 11",
        detail: "Windows 11 25H2 Enterprise",
        run: "notTaken",
        runDetail: "Not taken",
      },
    ],
  },
  {
    kind: "setVariable",
    name: "Name the computer",
    detail: "ComputerName = PC-{{SerialNumber|alnum|right:8}}",
    code: true,
    run: "done",
    runDetail: "ComputerName = PC-G2341KXQ",
  },
  {
    kind: "joinDomain",
    name: "Join the domain",
    detail: "With an account asked at the machine",
    run: "done",
    runDetail: "Took 14 s",
  },
  {
    kind: "group",
    name: "Berlin office",
    detail: "Only when Subnet is in 10.20.0.0/16",
    run: "done",
    inside: [
      {
        kind: "runScript",
        name: "Map the site share",
        detail: "Deploy share may not connect to fs01.berlin",
        problem: true,
        run: "done",
        runDetail: "Took 6 s",
      },
      {
        kind: "runScript",
        name: "Install the site printer",
        detail: "Only when Device kind is Desktop",
        run: "skipped",
        runDetail: "Skipped: this machine is a laptop",
      },
    ],
  },
  {
    kind: "repeat",
    name: "Wait for the share",
    detail: "Until LastStepFailed is No, at most 5 times",
    run: "running",
    runDetail: "Time 2 of at most 5",
    inside: [
      {
        kind: "runScript",
        name: "Test the share",
        detail: "Run script, cmd",
        run: "running",
        runDetail: "Running for 12 s",
      },
    ],
  },
  {
    kind: "pause",
    name: "Check the asset tag",
    detail: "Waits until someone lets the run go on",
    run: "paused",
    runDetail: "Paused for 4 min",
  },
  {
    kind: "reboot",
    name: "Restart",
    detail: "Restarts into the finished Windows",
    run: "waiting",
    runDetail: "Not started",
  },
];

// The demo as a sequence's steps, with ids built from their positions, and each node's demo data by id.
export function demoSequence(
  nodes: DemoNode[],
  prefix: string,
  byId: Map<string, DemoNode>,
): SequenceStep[] {
  return nodes.map((node, index) => {
    const id = `${prefix}${String(index)}`;
    const base = { ...newStep(node.kind, id), name: node.name };
    const inside = demoSequence(node.inside ?? [], `${id}.`, byId);
    const otherwise = demoSequence(node.otherwise ?? [], `${id}!`, byId);

    byId.set(id, node);

    switch (base.kind) {
      case "group":
      case "repeat":
        return { ...base, steps: inside };
      case "if":
        return { ...base, then: inside, else: otherwise };
      default:
        return base;
    }
  });
}
