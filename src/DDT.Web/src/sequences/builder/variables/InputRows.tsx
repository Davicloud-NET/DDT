// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { InputAsk } from "../../sequences";
import { DeclarationRow } from "./DeclarationRow";
import type { DeclarationRowsProps } from "./declarationRows";
import { InputFields } from "./InputFields";

function askedTag(ask: InputAsk) {
  switch (ask) {
    case "Web":
      return <Trans>Input, asked on the web</Trans>;
    case "Machine":
      return <Trans>Input, asked at the machine</Trans>;
    case "Both":
      return <Trans>Input, asked on the web or at the machine</Trans>;
  }
}

export function InputRows({
  draft,
  findings,
  isOpen,
  onToggle,
  onEdit,
  onGoToNode,
}: DeclarationRowsProps) {
  return draft.inputs.map((input, index) => (
    <DeclarationRow
      key={`i:${input.name}`}
      list="inputs"
      index={index}
      count={draft.inputs.length}
      name={input.name}
      tag={askedTag(input.askAt)}
      draft={draft}
      isOpen={isOpen("inputs", index)}
      onToggle={() => {
        onToggle("inputs", index);
      }}
      onEdit={onEdit}
      onGoToNode={onGoToNode}
    >
      <InputFields input={input} index={index} findings={findings} onEdit={onEdit} />
    </DeclarationRow>
  ));
}
