// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { ChoiceSetting } from "../../fields/ChoiceSetting";
import type { InputKind } from "../../sequences";
import type { InputPartProps } from "./declarationFields";

const inputKinds: InputKind[] = [
  "Text",
  "Choice",
  "MultiChoice",
  "YesNo",
  "Account",
  "Applications",
];

// The kind of answer an input takes. Switching kinds keeps only what the new kind can use: choices for a list, a
// destination for an account, what an Applications input offers.
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

  return (
    <ChoiceSetting
      label={<Trans>Answer</Trans>}
      field={at("kind")}
      findings={findings}
      value={input.kind}
      choices={inputKinds.map((kind) => ({ id: kind, label: kindLabels[kind] }))}
      onChange={(chosen) => {
        const kind = chosen as InputKind;
        const applications = kind === "Applications";

        update(
          {
            kind,
            account:
              kind === "Account"
                ? (input.account ?? { domain: null, hosts: [], runAs: false })
                : null,
            choices: kind === "Choice" || kind === "MultiChoice" ? input.choices : [],
            offered: applications ? (input.offered ?? []) : null,
            ticked: applications ? (input.ticked ?? []) : null,
            ...(applications ? { default: null, maxLength: null } : {}),
          },
          true,
        );
      }}
    />
  );
}
