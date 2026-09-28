// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { ChoiceSetting } from "../../fields/ChoiceSetting";
import { TextSetting } from "../../fields/TextSetting";
import { orNull, type InputPartProps } from "./declarationFields";

// The answer an input has until someone gives another: yes, no or none for a yes or no, else as it is typed.
export function InputDefault({ input, at, findings, update }: InputPartProps) {
  const { t } = useLingui();
  const listed = input.kind === "Choice" || input.kind === "MultiChoice";

  return input.kind === "YesNo" ? (
    <ChoiceSetting
      label={<Trans>Default</Trans>}
      field={at("default")}
      findings={findings}
      value={input.default ?? "none"}
      choices={[
        { id: "none", label: t`No default` },
        { id: "true", label: t`Yes` },
        { id: "false", label: t`No` },
      ]}
      onChange={(value) => {
        update({ default: value === "none" ? null : value }, true);
      }}
    />
  ) : (
    <TextSetting
      label={<Trans>Default</Trans>}
      field={at("default")}
      findings={findings}
      hint={
        listed ? <Trans>The value of a choice; several separated by semicolons.</Trans> : undefined
      }
      value={input.default ?? ""}
      onChange={(text) => {
        update({ default: orNull(text) });
      }}
    />
  );
}
