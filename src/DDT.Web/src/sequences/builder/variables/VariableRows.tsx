// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { DeclarationRow } from "./DeclarationRow";
import type { DeclarationRowsProps } from "./declarationRows";
import { VariableFields } from "./VariableFields";

export function VariableRows({
  draft,
  findings,
  isOpen,
  onToggle,
  onEdit,
  onGoToNode,
}: DeclarationRowsProps) {
  return draft.variables.map((variable, index) => (
    <DeclarationRow
      key={`v:${variable.name}`}
      list="variables"
      index={index}
      count={draft.variables.length}
      name={variable.name}
      tag={<Trans>Variable</Trans>}
      draft={draft}
      isOpen={isOpen("variables", index)}
      onToggle={() => {
        onToggle("variables", index);
      }}
      onEdit={onEdit}
      onGoToNode={onGoToNode}
    >
      <VariableFields variable={variable} index={index} findings={findings} onEdit={onEdit} />
    </DeclarationRow>
  ));
}
