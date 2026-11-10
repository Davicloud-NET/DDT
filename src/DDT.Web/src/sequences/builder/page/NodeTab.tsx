// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { placeLabel } from "../../flow/flowLabels";
import { stepFindings } from "../../problems";
import { bootPhase, sequenceKind } from "../../sequenceKinds";
import { NodeInspector } from "../NodeInspector";
import type { FlowBuilderModel } from "./useFlowBuilder";

// The inspector's Node tab: the chosen node's fields.
export function NodeTab({ model }: { model: FlowBuilderModel }) {
  const { selected, editor, index, edits, command } = model;

  if (selected === undefined) {
    return (
      <p className="type-small text-muted">
        <Trans>No node chosen.</Trans>
      </p>
    );
  }

  const sequence = sequenceKind(editor.draft.steps);

  return (
    <NodeInspector
      node={selected.node}
      place={placeLabel(index, selected)}
      sequence={sequence}
      phases={editor.phases.get(selected.node.id) ?? [bootPhase(sequence)]}
      findings={stepFindings(editor.findings, selected.node.id)}
      catalog={editor.catalog}
      onEdit={edits.edit}
      onRemove={() => {
        edits.remove(selected.node.id);
      }}
      onWrap={() => {
        edits.wrap(selected.node.id, "group");
      }}
      onUnwrap={() => {
        edits.unwrap(selected.node.id);
      }}
      onShift={(by) => {
        command({ type: "shift", by }, selected.node.id);
      }}
    />
  );
}
