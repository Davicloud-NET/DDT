// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { AccountSetting } from "../builder/AccountSetting";
import { kindOfPhase } from "../sequenceKinds";
import type { RunScriptStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

// The account a script runs as. Only Windows has one to offer, so RunScriptFields shows this elsewhere only while
// the step still names an account, to remove it.
export function ScriptRunAsSetting({
  step,
  findings,
  onChange,
}: Pick<KindFieldsProps<RunScriptStep>, "step" | "findings" | "onChange">) {
  const root = kindOfPhase(step.phase) === "Linux" ? t`root` : t`SYSTEM`;

  return (
    <AccountSetting
      label={<Trans>Run as</Trans>}
      field="runAs"
      findings={findings}
      className="sm:col-span-2"
      use="runAs"
      noneLabel={root}
      hint={
        step.phase === "Windows" ? (
          <Trans>An account in the installed Windows, signed in for the script alone.</Trans>
        ) : step.phase === "LinuxBoot" ? (
          <Trans>
            Only a script in Windows runs as an account; in the Linux boot environment it runs as
            root.
          </Trans>
        ) : step.phase === "Linux" ? (
          <Trans>
            Only a script in Windows runs as an account; in the installed Linux it runs as root.
          </Trans>
        ) : (
          <Trans>
            Only a script in Windows runs as an account; in Windows PE it runs as SYSTEM.
          </Trans>
        )
      }
      value={step.runAs ?? null}
      onChange={(runAs) => {
        onChange({ runAs }, true);
      }}
    />
  );
}
