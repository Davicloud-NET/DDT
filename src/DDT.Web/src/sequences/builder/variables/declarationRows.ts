// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { FlowEdit } from "../../flow/flowEdits";
import type { Findings } from "../../problems";
import type { SequenceDraft } from "../../sequenceDraft";

// A row of the variables panel, as a finding points at it.
export interface OpenRow {
  list: "variables" | "inputs";
  index: number;
}

export function rowKey(list: OpenRow["list"], index: number): string {
  return `${list}:${String(index)}`;
}

// What the list of variables and the list of inputs get from the panel.
export interface DeclarationRowsProps {
  draft: SequenceDraft;
  // The sequence's own findings, such as variables[1].name.
  findings: Findings;
  isOpen: (list: OpenRow["list"], index: number) => boolean;
  onToggle: (list: OpenRow["list"], index: number) => void;
  onEdit: (edit: FlowEdit) => void;
  onGoToNode: (id: string) => void;
}
