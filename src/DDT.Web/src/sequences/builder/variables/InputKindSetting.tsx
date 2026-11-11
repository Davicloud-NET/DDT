// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { ChoiceSetting } from "../../fields/ChoiceSetting";
import type { InputKind } from "../../sequences";
import type { InputPartProps } from "./declarationFields";

const inputKinds: InputKind[] = ["Text", "Choice", "MultiChoice", "YesNo", "Account"];

// The kind of answer an input takes. Switching kinds keeps only what the new kind can use: choices for a list, a
// destination for an account. Applications has no editor yet, so it's only listed for an input that has it.
export function InputKindSetting({ input, at, findings, update }: InputPartProps) {
  const { t } = useLingui();
  const kindLabels: Record<InputKind, string> = {
    Text: t`Text`,
    Choice: t`One of a list`,
    MultiChoice: t`Several of a list`,
    YesNo: t`Yes or no`,
    Account: t`An account, for the run alone`,
    Applications: t`Applications from the library`,
  };
  const offered: InputKind[] =
    input.kind === "Applications" ? [...inputKinds, "Applications"] : inputKinds;

  return (
    <ChoiceSetting
      label={<Trans>Answer</Trans>}
      field={at("kind")}
      findings={findings}
      value={input.kind}
      choices={offered.map((kind) => ({ id: kind, label: kindLabels[kind] }))}
      onChange={(chosen) => {
        const kind = chosen as InputKind;

        update(
          {
            kind,
            account:
              kind === "Account"
                ? (input.account ?? { domain: null, hosts: [], runAs: false })
                : null,
            choices: kind === "Choice" || kind === "MultiChoice" ? input.choices : [],
            offered: kind === "Applications" ? (input.offered ?? null) : null,
            ticked: kind === "Applications" ? (input.ticked ?? null) : null,
          },
          true,
        );
      }}
    />
  );
}
