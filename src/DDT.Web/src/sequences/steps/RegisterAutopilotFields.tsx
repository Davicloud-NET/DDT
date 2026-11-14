// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { FlagSetting } from "../fields/FlagSetting";
import { NumberSetting } from "../fields/NumberSetting";
import { TextSetting } from "../fields/TextSetting";
import type { RegisterAutopilotStep } from "../sequences";
import { orNull, type KindFieldsProps } from "./kindFields";

export function RegisterAutopilotFields({
  step,
  findings,
  onChange,
}: KindFieldsProps<RegisterAutopilotStep>) {
  return (
    <>
      <TextSetting
        label={<Trans>Group tag</Trans>}
        field="groupTag"
        findings={findings}
        hint={<Trans>Empty takes the default from the Autopilot settings.</Trans>}
        className="sm:col-span-2"
        maxLength={128}
        value={step.groupTag ?? ""}
        onChange={(groupTag) => {
          onChange({ groupTag: orNull(groupTag) });
        }}
      />
      <FlagSetting
        label={<Trans>Wait until Autopilot has imported the hash</Trans>}
        field="waitForImport"
        findings={findings}
        className="sm:col-span-2"
        value={step.waitForImport}
        onChange={(waitForImport) => {
          onChange({ waitForImport }, true);
        }}
      />
      {step.waitForImport && (
        <NumberSetting
          label={<Trans>Wait at most (minutes)</Trans>}
          field="waitMinutes"
          findings={findings}
          hint={<Trans>From 1 to 120.</Trans>}
          minValue={1}
          maxValue={120}
          value={step.waitMinutes}
          onChange={(waitMinutes) => {
            onChange({ waitMinutes });
          }}
        />
      )}
    </>
  );
}
