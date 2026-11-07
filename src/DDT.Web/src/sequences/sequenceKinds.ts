// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { walk } from "./flow/flowTree";
import type { ScriptInterpreter, SequenceKind, SequencePhase, SequenceStep } from "./sequences";

// The server's SequenceKinds: what a sequence installs, and the phases and interpreters that go with it.

// A sequence that writes a raw disk image anywhere in its tree is a Linux sequence.
export function sequenceKind(steps: readonly SequenceStep[]): SequenceKind {
  return kindOfNodes(walk(steps).map((entry) => entry.node));
}

// The same for nodes already taken out of their tree, such as those of a run's path.
export function kindOfNodes(nodes: readonly Pick<SequenceStep, "kind">[]): SequenceKind {
  return nodes.some((node) => node.kind === "writeRawImage") ? "Linux" : "Windows";
}

// Where a run of the kind starts.
export function bootPhase(kind: SequenceKind): SequencePhase {
  return kind === "Linux" ? "LinuxBoot" : "WindowsPE";
}

// A phase of a boot environment, before any hand-over to an installed system.
export function isBootPhase(phase: SequencePhase): boolean {
  return phase === "WindowsPE" || phase === "LinuxBoot";
}

// The kind whose sequences run in the phase.
export function kindOfPhase(phase: SequencePhase): SequenceKind {
  return phase === "LinuxBoot" ? "Linux" : "Windows";
}

// The phases a script of the kind can run in, in the order a run meets them.
export function scriptPhases(kind: SequenceKind): readonly SequencePhase[] {
  return kind === "Linux" ? ["LinuxBoot"] : ["WindowsPE", "Windows"];
}

// What runs a script of the kind. The first is what a new script starts with.
export function scriptInterpreters(kind: SequenceKind): readonly ScriptInterpreter[] {
  return kind === "Linux" ? ["Shell"] : ["Cmd", "PowerShell"];
}
