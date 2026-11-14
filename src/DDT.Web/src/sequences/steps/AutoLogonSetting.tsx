// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { AccountSetting } from "../builder/AccountSetting";
import { ChoiceSetting } from "../fields/ChoiceSetting";
import { NumberSetting } from "../fields/NumberSetting";
import type { StepPatch } from "../sequenceEdits";
import type { AutoLogonKind, WriteUnattendStep } from "../sequences";
import type { Findings } from "../problems";

// Who Windows signs in as by itself after the deployment, and how often.
export function AutoLogonSetting({
  step,
  findings,
  onChange,
}: {
  step: WriteUnattendStep;
  findings: Findings;
  onChange: (patch: StepPatch, chosen?: boolean) => void;
}) {
  const { t } = useLingui();

  return (
    <>
      <ChoiceSetting
        label={<Trans>Sign in automatically</Trans>}
        field="autoLogon"
        findings={findings}
        value={step.autoLogon}
        choices={[
          { id: "None", label: t`No` },
          { id: "LocalAdministrator", label: t`As the local administrator` },
          { id: "Account", label: t`As an account` },
        ]}
        onChange={(autoLogon) => {
          onChange({ autoLogon: autoLogon as AutoLogonKind }, true);
        }}
      />
      {step.autoLogon === "None" ? null : (
        <NumberSetting
          label={<Trans>How often</Trans>}
          field="autoLogonCount"
          findings={findings}
          hint={<Trans>From 1 to 99.</Trans>}
          minValue={1}
          maxValue={99}
          value={step.autoLogonCount}
          onChange={(autoLogonCount) => {
            onChange({ autoLogonCount });
          }}
        />
      )}
      {step.autoLogon === "Account" ? (
        <AccountSetting
          label={<Trans>Account</Trans>}
          field="autoLogonAccount"
          findings={findings}
          className="sm:col-span-2"
          use="autoLogon"
          value={step.autoLogonAccount ?? null}
          onChange={(autoLogonAccount) => {
            onChange({ autoLogonAccount }, true);
          }}
        />
      ) : null}
    </>
  );
}
