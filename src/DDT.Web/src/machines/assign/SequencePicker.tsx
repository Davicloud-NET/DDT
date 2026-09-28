// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { canRun, type SequenceSummary } from "@/sequences/sequences";
import { ListBoxItem, Select } from "@/ui/Select";

import { problemsText } from "./assignText";

interface SequencePickerProps {
  sequences: readonly SequenceSummary[];
  sequence: SequenceSummary | null;
  isDisabled: boolean;
  // Why the rules choose a sequence for this machine, or null if they don't.
  hint: string | null;
  onChange: (id: string | null) => void;
}

// Lists every sequence. Those with problems are shown but can't be picked.
export function SequencePicker({
  sequences,
  sequence,
  isDisabled,
  hint,
  onChange,
}: SequencePickerProps) {
  return (
    <Select
      label={<Trans>Task sequence</Trans>}
      value={sequence?.id ?? null}
      isDisabled={isDisabled}
      onChange={(key) => {
        onChange(key === null ? null : String(key));
      }}
      {...(hint === null ? {} : { hint })}
    >
      {sequences.map((candidate) => (
        <ListBoxItem
          key={candidate.id}
          id={candidate.id}
          isDisabled={!canRun(candidate)}
          {...(canRun(candidate) ? {} : { description: problemsText(candidate) })}
        >
          {candidate.name}
        </ListBoxItem>
      ))}
    </Select>
  );
}
