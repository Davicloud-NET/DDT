// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { ConditionBuilder } from "@/conditions/ConditionBuilder";
import { legacyPath, legacyTree } from "@/conditions/conditions";

import type { Findings } from "../../problems";
import type { SequenceEdit, StepPatch } from "../../sequenceEdits";
import type { SequenceStep } from "../../sequences";
import { isContainer } from "../../steps";
import { useBuilder } from "../builderData";
import { RepeatSettings } from "./RepeatSettings";
import { whenEdit } from "./whenEdit";

// The node's conditions: an IF's test, a Repeat's until and limit, and the when of every other node.
export function NodeConditions({
  node,
  findings,
  onEdit,
  change,
}: {
  node: SequenceStep;
  findings: Findings;
  onEdit: (edit: SequenceEdit) => void;
  change: (patch: StepPatch) => void;
}) {
  const { subjects } = useBuilder();
  const container = isContainer(node);

  return (
    <>
      {node.kind === "if" ? (
        <ConditionBuilder
          label={<Trans>Go along Then when</Trans>}
          hint={<Trans>Every other machine goes along Else.</Trans>}
          use="test"
          field="test"
          value={node.test}
          subjects={subjects}
          findings={findings}
          onChange={(path, condition) => {
            onEdit({ type: "editCondition", id: node.id, field: "test", path, change: condition });
          }}
        />
      ) : null}
      {node.kind === "repeat" ? (
        <RepeatSettings
          node={node}
          subjects={subjects}
          findings={findings}
          onEdit={onEdit}
          change={change}
        />
      ) : null}
      {node.kind === "if" ? null : (
        <ConditionBuilder
          label={container ? <Trans>Run it only when</Trans> : <Trans>Run only when</Trans>}
          use="when"
          value={legacyTree(node.conditions, node.when)}
          subjects={subjects}
          findings={findings}
          fieldOf={(path) => legacyPath(node.conditions.length, (node.when ?? null) !== null, path)}
          onChange={(path, condition) => {
            const edit = whenEdit(node, path, condition);

            if (edit !== null) {
              onEdit(edit);
            }
          }}
        />
      )}
    </>
  );
}
