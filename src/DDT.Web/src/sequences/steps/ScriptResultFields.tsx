// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { CodesSetting } from "../fields/CodesSetting";
import { NumberSetting } from "../fields/NumberSetting";
import type { RunScriptStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

// The timeout and the exit codes, which decide how a script's step ends.
export function ScriptResultFields({
  step,
  findings,
  onChange,
}: Pick<KindFieldsProps<RunScriptStep>, "step" | "findings" | "onChange">) {
  return (
    <>
      <NumberSetting
        label={<Trans>Timeout in minutes</Trans>}
        field="timeoutMinutes"
        findings={findings}
        hint={<Trans>The step fails when the script runs longer.</Trans>}
        minValue={1}
        value={step.timeoutMinutes}
        onChange={(timeoutMinutes) => {
          onChange({ timeoutMinutes });
        }}
      />
      <CodesSetting
        label={<Trans>Exit codes that mean success</Trans>}
        field="successExitCodes"
        findings={findings}
        hint={<Trans>Whole numbers, separated by commas.</Trans>}
        value={step.successExitCodes}
        onChange={(successExitCodes) => {
          onChange({ successExitCodes });
        }}
      />
      <CodesSetting
        label={<Trans>Exit codes that ask for a restart</Trans>}
        field="rebootExitCodes"
        findings={findings}
        hint={<Trans>The machine restarts and the sequence goes on with the next step.</Trans>}
        value={step.rebootExitCodes}
        onChange={(rebootExitCodes) => {
          onChange({ rebootExitCodes });
        }}
      />
    </>
  );
}
