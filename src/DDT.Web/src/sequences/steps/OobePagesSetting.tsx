// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { FlagSetting } from "../fields/FlagSetting";
import type { WriteUnattendStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

// The out-of-box pages Windows skips at its first start.
export function OobePagesSetting({
  step,
  findings,
  onChange,
}: Pick<KindFieldsProps<WriteUnattendStep>, "step" | "findings" | "onChange">) {
  return (
    <>
      <FlagSetting
        label={<Trans>Skip the licence page</Trans>}
        field="hideEulaPage"
        findings={findings}
        value={step.hideEulaPage}
        onChange={(hideEulaPage) => {
          onChange({ hideEulaPage }, true);
        }}
      />
      <FlagSetting
        label={<Trans>Skip the OEM registration page</Trans>}
        field="hideOemRegistration"
        findings={findings}
        value={step.hideOemRegistration}
        onChange={(hideOemRegistration) => {
          onChange({ hideOemRegistration }, true);
        }}
      />
      <FlagSetting
        label={<Trans>Skip the online account pages</Trans>}
        field="hideOnlineAccounts"
        findings={findings}
        value={step.hideOnlineAccounts}
        onChange={(hideOnlineAccounts) => {
          onChange({ hideOnlineAccounts }, true);
        }}
      />
      <FlagSetting
        label={<Trans>Skip the wireless page</Trans>}
        field="hideWirelessSetup"
        findings={findings}
        value={step.hideWirelessSetup}
        onChange={(hideWirelessSetup) => {
          onChange({ hideWirelessSetup }, true);
        }}
      />
    </>
  );
}
