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

// Where a run of the kind goes on after the hand-over.
export function installedPhase(kind: SequenceKind): SequencePhase {
  return kind === "Linux" ? "Linux" : "Windows";
}

// The kind whose sequences run in the phase.
export function kindOfPhase(phase: SequencePhase): SequenceKind {
  return phase === "LinuxBoot" || phase === "Linux" ? "Linux" : "Windows";
}

// The phases a script of the kind can run in, in the order a run meets them.
export function scriptPhases(kind: SequenceKind): readonly SequencePhase[] {
  return kind === "Linux" ? ["LinuxBoot", "Linux"] : ["WindowsPE", "Windows"];
}

// Whether a step that may run in the phases can connect shares. The Linux boot environment connects none, so a step
// that may run there cannot, even if it may also run in the installed Linux.
export function connectsShares(kind: SequenceKind, phases: readonly SequencePhase[]): boolean {
  return kind !== "Linux" || !phases.some(isBootPhase);
}

// The exit codes that ask for a restart, as a new script in the phase has them. The Linux boot environment cannot
// restart, and a shell makes 194 of exit 3010.
export function restartCodes(phase: SequencePhase): number[] {
  return phase === "LinuxBoot" ? [] : phase === "Linux" ? [194] : [3010];
}

// What runs a script of the kind. The first is what a new script starts with.
export function scriptInterpreters(kind: SequenceKind): readonly ScriptInterpreter[] {
  return kind === "Linux" ? ["Shell"] : ["Cmd", "PowerShell"];
}
