// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { FlagSetting } from "../../fields/FlagSetting";
import { TextSetting } from "../../fields/TextSetting";
import type { FlowEdit, VariablePatch } from "../../flow/flowEdits";
import type { Findings } from "../../problems";
import type { VariableDeclaration } from "../../sequences";
import { TemplateField } from "../TemplateField";
import { orNull } from "./declarationFields";

export function VariableFields({
  variable,
  index,
  findings,
  onEdit,
}: {
  variable: VariableDeclaration;
  index: number;
  findings: Findings;
  onEdit: (edit: FlowEdit) => void;
}) {
  const at = (member: string) => `variables[${String(index)}].${member}`;
  const name = variable.name;
  const update = (patch: VariablePatch, chosen = false) => {
    onEdit({ type: "updateVariable", name, patch, ...(chosen ? { chosen } : {}) });
  };

  return (
    <>
      <TemplateField
        label={<Trans>Default</Trans>}
        field={at("default")}
        findings={findings}
        value={variable.default ?? ""}
        onChange={(text) => {
          update({ default: orNull(text) });
        }}
      />
      <TextSetting
        label={<Trans>Description</Trans>}
        field={at("description")}
        findings={findings}
        value={variable.description ?? ""}
        onChange={(text) => {
          update({ description: orNull(text) });
        }}
      />
      <FlagSetting
        label={<Trans>Steps may change it</Trans>}
        field={at("setBySteps")}
        findings={findings}
        value={variable.setBySteps}
        onChange={(setBySteps) => {
          update({ setBySteps }, true);
        }}
      />
    </>
  );
}
