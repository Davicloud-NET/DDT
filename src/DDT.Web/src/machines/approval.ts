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
  // The sequence the approval runs, or null if it runs none. The dialog keeps its warnings about it up to date as
  // the machine changes.
  sequence: SequenceSummary | null;
  // The sequence's inputs that are asked on the web, and the values their fields start with for this machine. The
  // approval sends the answers.
  inputs: AskedInput[];
  defaults: ResolvedValue[];
}

// A rule never authorizes a machine. Approving is the first human decision to run the rule's choice, so the operator
// is told what it does. Returns null if nothing needs confirming: no rule chooses, or someone signed in at the
// machine and chooses there. If the server would refuse the run, the approval only authorizes the machine.
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
  // The rule's words for the start of a sentence and for the middle of one.
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

  // The computer name from the machine's values. It's used when the sequence needs a name and the machine has none.
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
