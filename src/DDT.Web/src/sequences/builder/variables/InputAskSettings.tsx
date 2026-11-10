// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { ChoiceSetting } from "../../fields/ChoiceSetting";
import { FlagSetting } from "../../fields/FlagSetting";
import type { InputAsk } from "../../sequences";
import type { InputPartProps } from "./declarationFields";

const askAts: InputAsk[] = ["Web", "Machine", "Both"];

// Where an input is asked, and whether it must be answered.
export function InputAskSettings({ input, at, findings, update }: InputPartProps) {
  const { t } = useLingui();
  const askLabels: Record<InputAsk, string> = {
    Web: t`On the web`,
    Machine: t`At the machine`,
    Both: t`On the web or at the machine`,
  };

  return (
    <>
      <ChoiceSetting
        label={<Trans>Asked</Trans>}
        field={at("askAt")}
        findings={findings}
        value={input.askAt}
        choices={askAts.map((ask) => ({ id: ask, label: askLabels[ask] }))}
        onChange={(ask) => {
          update({ askAt: ask as InputAsk }, true);
        }}
      />
      <FlagSetting
        label={<Trans>An answer is required</Trans>}
        field={at("required")}
        findings={findings}
        hint={
          input.askAt === "Web" ? (
            <Trans>
              Without one, and without a default, a run chosen at the machine or started by a rule
              fails at its start.
            </Trans>
          ) : undefined
        }
        value={input.required}
        onChange={(required) => {
          update({ required }, true);
        }}
      />
    </>
  );
}
