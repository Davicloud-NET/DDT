// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { FlagSetting } from "../fields/FlagSetting";
import { NumberSetting } from "../fields/NumberSetting";
import type { EnableBitLockerStep } from "../windowsSteps";
import type { KindFieldsProps } from "./kindFields";

export function EnableBitLockerFields({
  step,
  findings,
  onChange,
}: KindFieldsProps<EnableBitLockerStep>) {
  return (
    <>
      <FlagSetting
        label={<Trans>Fail without a TPM</Trans>}
        field="ifNoTpm"
        findings={findings}
        hint={<Trans>Off: the step is skipped with a warning.</Trans>}
        className="sm:col-span-2"
        value={step.ifNoTpm === "Fail"}
        onChange={(fail) => {
          onChange({ ifNoTpm: fail ? "Fail" : "Skip" });
        }}
      />
      <FlagSetting
        label={<Trans>Back up the recovery password to Active Directory</Trans>}
        field="backupToActiveDirectory"
        findings={findings}
        hint={<Trans>On a machine joined to a domain. DDT keeps the password either way.</Trans>}
        className="sm:col-span-2"
        value={step.backupToActiveDirectory}
        onChange={(backupToActiveDirectory) => {
          onChange({ backupToActiveDirectory });
        }}
      />
      <FlagSetting
        label={<Trans>Wait until the disk is encrypted</Trans>}
        field="waitForEncryption"
        findings={findings}
        className="sm:col-span-2"
        value={step.waitForEncryption}
        onChange={(waitForEncryption) => {
          onChange({ waitForEncryption }, true);
        }}
      />
      {step.waitForEncryption && (
        <NumberSetting
          label={<Trans>Wait at most (minutes)</Trans>}
          field="waitMinutes"
          findings={findings}
          hint={<Trans>From 1 to 1440.</Trans>}
          minValue={1}
          maxValue={1440}
          value={step.waitMinutes}
          onChange={(waitMinutes) => {
            onChange({ waitMinutes });
          }}
        />
      )}
    </>
  );
}
