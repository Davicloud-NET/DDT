// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";

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
  const name = resolution.sequenceName ?? t`a sequence`;
  const byMac = resolution.source === "MacRule";
  // The rule as the subject of a sentence, and inside one.
  const rule = byMac ? t`A rule for its MAC address` : t`A rule for its model`;
  const ruleInside = byMac ? t`a rule for its MAC address` : t`a rule for its model`;
  const sequence = sequences.find((candidate) => candidate.id === resolution.sequenceId);

  const withoutRun = (consequence: string): ApprovalPlan => ({
    expectedSequenceId: null,
    consequence,
    confirmLabel: t`Approve without a sequence`,
    sequence: null,
  });

  if (resolution.problemCount > 0) {
    const count = resolution.problemCount;
    const problems = plural(count, { one: "# problem", other: "# problems" });

    return withoutRun(
      t`${rule} chooses ${name}, but it has ${problems} and cannot run until they are fixed. Approving authorizes ${label} without running anything.`,
    );
  }

  if (sequence?.erasesDisk === true && (machine.eligibleDiskCount ?? 0) > 1) {
    return withoutRun(
      t`${rule} chooses ${name}, which erases a disk, and the machine has more than one; sign in at it to choose the disk. Approving authorizes ${label} without running anything.`,
    );
  }

  if (sequence?.needsComputerName === true && machine.assignedName === null) {
    return withoutRun(
      sequence.rawImageName === null
        ? t`${rule} chooses ${name}, which joins the domain, and the machine has no name yet; assign the sequence with a computer name. Approving authorizes ${label} without running anything.`
        : t`${rule} chooses ${name}, which names the machine in its cloud-init seed, and the machine has no name yet; assign the sequence with a computer name. Approving authorizes ${label} without running anything.`,
    );
  }

  const runs = t`Approving ${label} also runs ${name} on it, which ${ruleInside} chose.`;
  const effects =
    sequence === undefined
      ? ""
      : sequence.erasesDisk
        ? ` ${t`All data on its disk is erased.`}`
        : ` ${t`Its disk is not erased.`}`;

  return {
    expectedSequenceId: resolution.sequenceId,
    consequence: `${runs}${effects}`,
    confirmLabel: t`Approve and run ${name}`,
    sequence: sequence ?? null,
  };
}
