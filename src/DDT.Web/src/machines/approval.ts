// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";

import { webInputs, type AskedInput } from "@/inputs/inputs";
import { machineLabel, type MachineSummary } from "@/machines/machines";
import {
  isRuleChoice,
  ruleChoiceWords,
  valuesComputerName,
  type MachineSequenceResolution,
} from "@/rules/rules";
import type { SequenceSummary } from "@/sequences/sequences";
import type { ResolvedValue } from "@/values/values";

// What approving a waiting machine does when a rule chose its sequence.
export interface ApprovalPlan {
  // Sent with the approval, so the server runs the sequence only while the rules still choose it. Null
  // approves without running anything.
  expectedSequenceId: string | null;
  consequence: string;
  confirmLabel: string;
  // The sequence the approval runs, for what the dialog says of it as the machine changes; null when it runs none.
  sequence: SequenceSummary | null;
  // The inputs of that sequence asked on the web, which the approval sends answers to, and what their fields start
  // with for this machine.
  inputs: AskedInput[];
  defaults: ResolvedValue[];
}

// Null when the approval runs nothing and needs no confirmation: no rule chooses a sequence, or someone signed
// in at the machine, who chooses the sequence there. A rule never authorizes, so with a rule's choice the
// approval is the first human decision to run it, and the operator is told what it does. Where the server
// would refuse the run, the approval only authorizes the machine. A sequence that needs a computer name runs on a
// machine without one when its values give one, such as a rule's name pattern, and the operator is told that name.
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
  // The rule as the subject of a sentence, and inside one.
  const { subject: rule, inside: ruleInside } = ruleChoiceWords(resolution);
  const sequence = sequences.find((candidate) => candidate.id === resolution.sequenceId);

  const withoutRun = (consequence: string): ApprovalPlan => ({
    expectedSequenceId: null,
    consequence,
    confirmLabel: t`Approve without a sequence`,
    sequence: null,
    inputs: [],
    defaults: [],
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

  // The name the run gets, where it is not the machine's own.
  const named =
    sequence?.needsComputerName === true && machine.assignedName === null
      ? valuesComputerName(resolution)
      : null;

  if (sequence?.needsComputerName === true && machine.assignedName === null && named === null) {
    return withoutRun(
      sequence.rawImageName === null
        ? t`${rule} chooses ${name}, which joins the domain, and the machine has no name yet; assign the sequence with a computer name. Approving authorizes ${label} without running anything.`
        : t`${rule} chooses ${name}, which names the machine in its cloud-init seed, and the machine has no name yet; assign the sequence with a computer name. Approving authorizes ${label} without running anything.`,
    );
  }

  const runs = t`Approving ${label} also runs ${name} on it, which ${ruleInside} chose.`;
  const naming = named === null ? "" : ` ${t`It is named ${named}.`}`;
  const effects =
    sequence === undefined
      ? ""
      : sequence.erasesDisk
        ? ` ${t`All data on its disk is erased.`}`
        : ` ${t`Its disk is not erased.`}`;

  return {
    expectedSequenceId: resolution.sequenceId,
    consequence: `${runs}${naming}${effects}`,
    confirmLabel: t`Approve and run ${name}`,
    sequence: sequence ?? null,
    inputs: webInputs(resolution.inputs),
    defaults: resolution.inputDefaults ?? [],
  };
}
