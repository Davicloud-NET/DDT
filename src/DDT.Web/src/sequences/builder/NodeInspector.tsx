// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useContext, type KeyboardEvent } from "react";

import { movesFrom } from "../editorFocus";
import { EditorLock } from "../editorLock";
import { TextSetting } from "../fields/TextSetting";
import type { Findings } from "../problems";
import type { SequenceEdit, StepPatch } from "../sequenceEdits";
import type { SequencePhase, SequenceStep } from "../sequences";
import { StepFields } from "../steps/StepFields";
import { isContainer } from "../steps";
import type { StepCatalog } from "../useStepCatalog";
import { FailureSettings } from "./inspector/FailureSettings";
import { NodeConditions } from "./inspector/NodeConditions";
import { NodeKeys } from "./inspector/NodeKeys";
import { NodeSummary } from "./inspector/NodeSummary";
import { SharesSetting } from "./inspector/SharesSetting";
import { UnplacedFindings } from "./inspector/UnplacedFindings";

interface NodeInspectorProps {
  node: SequenceStep;
  // Where the node is, such as "Step 2 of Then of 'If: Is it a Latitude?'".
  place: string;
  phases: readonly SequencePhase[];
  // This node's findings.
  findings: Findings;
  catalog: StepCatalog;
  onEdit: (edit: SequenceEdit) => void;
  onRemove: () => void;
  onWrap: () => void;
  onUnwrap: () => void;
  onShift: (by: -1 | 1) => void;
}

// The node chosen in the flow, with everything it does. Alt with Up or Down in its fields moves it within its list,
// as on the canvas.
export function NodeInspector({
  node,
  place,
  phases,
  findings,
  catalog,
  onEdit,
  onRemove,
  onWrap,
  onUnwrap,
  onShift,
}: NodeInspectorProps) {
  const locked = useContext(EditorLock);
  const container = isContainer(node);

  const change = (patch: StepPatch, chosen?: boolean) => {
    onEdit({ type: "updateNode", id: node.id, patch, ...(chosen === true ? { chosen } : {}) });
  };

  const onKey = (event: KeyboardEvent) => {
    if (
      event.altKey &&
      (event.key === "ArrowUp" || event.key === "ArrowDown") &&
      movesFrom(event.target)
    ) {
      event.preventDefault();
      onShift(event.key === "ArrowUp" ? -1 : 1);
    }
  };

  return (
    // Keyed by the node, so what a field holds while it is typed in stays with its node.
    <div key={node.id} onKeyDown={onKey} className="flex flex-col gap-5">
      <NodeSummary node={node} place={place} phases={phases} findings={findings} />
      <UnplacedFindings node={node} findings={findings} />
      <TextSetting
        label={<Trans>Name</Trans>}
        field="name"
        findings={findings}
        hint={<Trans>Shown in the flow, in the run and in the log.</Trans>}
        value={node.name}
        onChange={(text) => {
          change({ name: text });
        }}
      />
      {container ? null : (
        <div className="flex flex-col gap-4">
          <StepFields step={node} findings={findings} catalog={catalog} onChange={change} />
        </div>
      )}
      <NodeConditions node={node} findings={findings} onEdit={onEdit} change={change} />
      {container ? null : (
        <SharesSetting
          shares={node.shares ?? []}
          findings={findings}
          onChange={(shares, chosen) => {
            change({ shares: shares.length === 0 ? null : shares }, chosen);
          }}
        />
      )}
      <FailureSettings node={node} findings={findings} change={change} />
      {locked ? null : (
        <NodeKeys
          name={node.name}
          container={container}
          onWrap={onWrap}
          onUnwrap={onUnwrap}
          onRemove={onRemove}
        />
      )}
    </div>
  );
}
