// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { placeLabel } from "../../flow/flowLabels";
import { stepFindings } from "../../problems";
import { NodeInspector } from "../NodeInspector";
import type { FlowBuilderModel } from "./useFlowBuilder";

// The inspector's Node tab: the chosen node's fields, or where to find them.
export function NodeTab({ model }: { model: FlowBuilderModel }) {
  const { selected, editor, index, edits, command } = model;

  if (selected === undefined) {
    return (
      <p className="type-small text-muted">
        {editor.draft.steps.length === 0 ? (
          <Trans>Add a step to the flow to see its settings here.</Trans>
        ) : (
          <Trans>Choose a node in the flow to see its settings here.</Trans>
        )}
      </p>
    );
  }

  return (
    <NodeInspector
      node={selected.node}
      place={placeLabel(index, selected)}
      phases={editor.phases.get(selected.node.id) ?? ["WindowsPE"]}
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
