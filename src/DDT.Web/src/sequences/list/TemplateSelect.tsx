// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { ListBoxItem, Select } from "@/ui/Select";

import type { SequenceTemplate } from "../sequences";
import { EMPTY_CHOICE } from "./sequenceTemplates";

interface TemplateSelectProps {
  templates: readonly SequenceTemplate[];
  chosen: string;
  // The template chosen, whose description is the hint; null for an empty sequence.
  template: SequenceTemplate | null;
  isDisabled: boolean;
  onChange: (choice: string | null) => void;
}

// What a new sequence starts from: one of the server's templates, or nothing.
export function TemplateSelect({
  templates,
  chosen,
  template,
  isDisabled,
  onChange,
}: TemplateSelectProps) {
  const { t } = useLingui();

  return (
    <Select
      label={<Trans>Start from</Trans>}
      value={chosen}
      isDisabled={isDisabled}
      onChange={(key) => {
        onChange(key === null ? null : String(key));
      }}
      hint={
        template?.description ?? (
          <Trans>An empty sequence, to which you add the steps in the editor.</Trans>
        )
      }
    >
      {templates.map((candidate) => (
        <ListBoxItem
          key={candidate.key}
          id={candidate.key}
          textValue={candidate.name}
          description={candidate.description}
        >
          {candidate.name}
        </ListBoxItem>
      ))}
      <ListBoxItem id={EMPTY_CHOICE} textValue={t`Empty sequence`}>
        {t`Empty sequence`}
      </ListBoxItem>
    </Select>
  );
}
