// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, upperFirst } from "@/lib/format";
import { machineLabel, type MachineSummary } from "@/machines/machines";

import { isRuleChoice, type MachineSequenceResolution } from "@/rules/rules";
import type { SequenceSummary } from "@/sequences/sequences";

// What approving a waiting machine does when a rule chose its sequence.
export interface ApprovalPlan {
  // Sent with the approval, so the server runs the sequence only while the rules still choose it. Null
  // approves without running anything.
  expectedSequenceId: string | null;
  consequence: string;
  confirmLabel: string;
  // The sequence the approval runs, for what the dialog says of it as the machine changes; null when it runs none.
  sequence: SequenceSummary | null;
}

// Null when the approval runs nothing and needs no confirmation: no rule chooses a sequence, or someone signed
// in at the machine, who chooses the sequence there. A rule never authorizes, so with a rule's choice the
// approval is the first human decision to run it, and the operator is told what it does. Where the server
// would refuse the run, the approval only authorizes the machine.
export function approvalPlan(
  machine: MachineSummary,
  resolution: MachineSequenceResolution,
  sequences: readonly SequenceSummary[],
): ApprovalPlan | null {
  if (machine.signedInBy !== null || !isRuleChoice(resolution)) {
    return null;
  }

  const label = machineLabel(machine);
  const name = resolution.sequenceName ?? "a sequence";
  const rule =
    resolution.source === "MacRule" ? "a rule for its MAC address" : "a rule for its model";
  const sequence = sequences.find((candidate) => candidate.id === resolution.sequenceId);

  const withoutRun = (why: string): ApprovalPlan => ({
    expectedSequenceId: null,
    consequence: `${upperFirst(rule)} chooses ${name}, ${why}. Approving authorizes ${label} without running anything.`,
    confirmLabel: "Approve without a sequence",
    sequence: null,
  });

  if (resolution.problemCount > 0) {
    return withoutRun(
      `but it has ${plural(resolution.problemCount, "problem")} and cannot run until they are fixed`,
    );
  }

  if (sequence?.erasesDisk === true && (machine.eligibleDiskCount ?? 0) > 1) {
    return withoutRun(
      "which erases a disk, and the machine has more than one; sign in at it to choose the disk",
    );
  }

  if (sequence?.needsComputerName === true && machine.assignedName === null) {
    const use =
      sequence.rawImageName === null
        ? "joins the domain"
        : "names the machine in its cloud-init seed";

    return withoutRun(
      `which ${use}, and the machine has no name yet; assign the sequence with a computer name`,
    );
  }

  const effects =
    sequence === undefined
      ? ""
      : sequence.erasesDisk
        ? " All data on its disk is erased."
        : " Its disk is not erased.";

  return {
    expectedSequenceId: resolution.sequenceId,
    consequence: `Approving ${label} also runs ${name} on it, which ${rule} chose.${effects}`,
    confirmLabel: `Approve and run ${name}`,
    sequence: sequence ?? null,
  };
}
