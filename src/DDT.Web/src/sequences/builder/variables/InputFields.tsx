// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { TextSetting } from "../../fields/TextSetting";
import type { FlowEdit, InputPatch } from "../../flow/flowEdits";
import type { Findings } from "../../problems";
import type { InputDeclaration } from "../../sequences";
import { AccountInputFields } from "./AccountInputFields";
import { orNull, type InputPartProps } from "./declarationFields";
import { InputAskSettings } from "./InputAskSettings";
import { InputChoices } from "./InputChoices";
import { InputDefault } from "./InputDefault";
import { InputKindSetting } from "./InputKindSetting";
import { MaxLengthSetting } from "./MaxLengthSetting";

// An input's question, its kind of answer with what that kind takes, and where it is asked.
export function InputFields({
  input,
  index,
  findings,
  onEdit,
}: {
  input: InputDeclaration;
  index: number;
  findings: Findings;
  onEdit: (edit: FlowEdit) => void;
}) {
  const name = input.name;
  const update = (patch: InputPatch, chosen = false) => {
    onEdit({ type: "updateInput", name, patch, ...(chosen ? { chosen } : {}) });
  };
  const part: InputPartProps = {
    input,
    at: (member: string) => `inputs[${String(index)}].${member}`,
    findings,
    update,
  };
  const listed = input.kind === "Choice" || input.kind === "MultiChoice";

  return (
    <>
      <TextSetting
        label={<Trans>Question</Trans>}
        field={part.at("label")}
        findings={findings}
        hint={<Trans>What the person who answers reads.</Trans>}
        value={input.label}
        onChange={(label) => {
          update({ label });
        }}
      />
      <TextSetting
        label={<Trans>Help</Trans>}
        field={part.at("help")}
        findings={findings}
        value={input.help ?? ""}
        onChange={(text) => {
          update({ help: orNull(text) });
        }}
      />
      <InputKindSetting {...part} />
      {listed ? <InputChoices {...part} /> : null}
      {input.kind === "Account" ? <AccountInputFields {...part} /> : <InputDefault {...part} />}
      {input.kind === "Text" ? <MaxLengthSetting {...part} /> : null}
      <InputAskSettings {...part} />
    </>
  );
}
