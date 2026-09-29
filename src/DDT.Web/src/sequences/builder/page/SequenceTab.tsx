// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { AutosaveState } from "@/lib/autosave";

import { TextSetting } from "../../fields/TextSetting";
import type { Findings } from "../../problems";
import type { SequenceDraft } from "../../sequenceDraft";
import type { SequenceEdit } from "../../sequenceEdits";

// The server's refusal of the name or the description, such as a name another sequence has.
function refused(state: AutosaveState, field: string): Findings {
  const messages = state.kind === "refused" ? (state.problem?.errors?.[field] ?? []) : [];

  return { problems: messages.map((message) => ({ stepId: null, field, message })), warnings: [] };
}

interface SequenceTabProps {
  state: AutosaveState;
  draft: SequenceDraft;
  onEdit: (change: SequenceEdit) => void;
}

// The inspector's Sequence tab: the sequence's own name and description.
export function SequenceTab({ state, draft, onEdit }: SequenceTabProps) {
  return (
    <>
      <TextSetting
        label={<Trans>Sequence name</Trans>}
        field="name"
        findings={refused(state, "name")}
        value={draft.name}
        onChange={(text) => {
          onEdit({ type: "rename", name: text });
        }}
      />
      <TextSetting
        label={<Trans>Description</Trans>}
        field="description"
        findings={refused(state, "description")}
        hint={<Trans>Shown in the list of task sequences.</Trans>}
        multiline
        rows={3}
        value={draft.description}
        onChange={(text) => {
          onEdit({ type: "describe", description: text });
        }}
      />
    </>
  );
}
