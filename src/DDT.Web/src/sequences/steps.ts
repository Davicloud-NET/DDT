// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequencePhase, StepKind } from "./sequences";

const kindLabels: Record<StepKind, string> = {
  partition: "Partition the disk",
  applyImage: "Apply image",
  injectDrivers: "Inject drivers",
  writeUnattend: "Write the answer file",
  joinDomain: "Join the domain",
  runScript: "Run script",
  reboot: "Restart",
};

// A run names its steps' kinds as text, so a kind this page does not know yet is shown as it is.
export function stepKindLabel(kind: string): string {
  return Object.hasOwn(kindLabels, kind) ? kindLabels[kind as StepKind] : kind;
}

export function phaseLabel(phase: SequencePhase): string {
  return phase === "WindowsPE" ? "Windows PE" : "Windows";
}
