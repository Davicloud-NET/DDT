// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useContext } from "react";

import { EditorLock } from "../editorLock";
import type { FlowEdit } from "../flow/flowEdits";
import type { Findings } from "../problems";
import type { SequenceDraft } from "../sequenceDraft";
import { AddDeclarationKeys } from "./variables/AddDeclarationKeys";
import type { OpenRow } from "./variables/declarationRows";
import { InputRows } from "./variables/InputRows";
import { useOpenRows } from "./variables/useOpenRows";
import { VariableRows } from "./variables/VariableRows";

export type { OpenRow } from "./variables/declarationRows";

interface VariablesPanelProps {
  draft: SequenceDraft;
  // The sequence's own findings, such as variables[1].name.
  findings: Findings;
  // The row a finding points at, which the panel opens.
  open: OpenRow | null;
  onEdit: (edit: FlowEdit) => void;
  onGoToNode: (id: string) => void;
}

// The sequence's variables and inputs in one list, as the document holds them. Each shows which nodes use it. A
// rename changes all of those nodes.
export function VariablesPanel({ draft, findings, open, onEdit, onGoToNode }: VariablesPanelProps) {
  const locked = useContext(EditorLock);
  const rows = useOpenRows(open);
  const empty = draft.variables.length === 0 && draft.inputs.length === 0;
  const lists = {
    draft,
    findings,
    isOpen: rows.isOpen,
    onToggle: rows.toggle,
    onEdit,
    onGoToNode,
  };

  return (
    <div className="flex flex-col gap-4">
      <p className="type-small text-ink-2">
        <Trans>
          A variable is a value with a default, which rules, machine roles and steps may change. An
          input is asked on the web or at the machine before the run starts, and its answer sets the
          variable of its name.
        </Trans>
      </p>
      {empty ? (
        <p className="type-small text-muted">
          <Trans>This sequence declares no variables or inputs yet.</Trans>
        </p>
      ) : (
        <ul className="flex flex-col gap-2">
          <VariableRows {...lists} />
          <InputRows {...lists} />
        </ul>
      )}
      {locked ? null : <AddDeclarationKeys draft={draft} onEdit={onEdit} onAdded={rows.add} />}
    </div>
  );
}
