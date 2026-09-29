// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import { ChoiceSetting } from "@/sequences/fields/ChoiceSetting";
import { canRun, type SequenceSummary } from "@/sequences/sequences";

import type { RuleForm } from "./useRuleForm";

const NO_SEQUENCE = "none";

// The task sequence a rule chooses, if any.
export function SequenceChoice({
  form,
  sequences,
}: {
  form: RuleForm;
  sequences: readonly SequenceSummary[];
}) {
  const { t } = useLingui();
  const { base, edit, findings, change } = form;

  const choices = [
    { id: NO_SEQUENCE, label: t`None, a later rule may choose one` },
    ...sequences.map((sequence) => {
      const count = sequence.problemCount;
      const problems = plural(count, { one: "# problem", other: "# problems" });

      return {
        id: sequence.id,
        label: sequence.name,
        // A sequence with problems can't run, so it can't be chosen. A rule that already chose it keeps it until
        // another one is chosen.
        isDisabled: !canRun(sequence) && sequence.id !== edit.sequenceId,
        ...(canRun(sequence) ? {} : { description: t`${problems}, cannot run` }),
      };
    }),
    ...(edit.sequenceId !== null && !sequences.some((sequence) => sequence.id === edit.sequenceId)
      ? [{ id: edit.sequenceId, label: base?.sequenceName ?? t`A sequence that is gone` }]
      : []),
  ];

  return (
    <ChoiceSetting
      label={<Trans>Chooses a task sequence</Trans>}
      field="sequenceId"
      findings={findings}
      value={edit.sequenceId ?? NO_SEQUENCE}
      choices={choices}
      onChange={(id) => {
        change({ sequenceId: id === NO_SEQUENCE ? null : id });
      }}
    />
  );
}
