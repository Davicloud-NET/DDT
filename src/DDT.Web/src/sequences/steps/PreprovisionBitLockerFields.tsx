// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { FlagSetting } from "../fields/FlagSetting";
import type { PreprovisionBitLockerStep } from "../windowsSteps";
import type { KindFieldsProps } from "./kindFields";

export function PreprovisionBitLockerFields({
  step,
  findings,
  onChange,
}: KindFieldsProps<PreprovisionBitLockerStep>) {
  return (
    <FlagSetting
      label={<Trans>Fail without a TPM or a boot image with BitLocker</Trans>}
      field="ifUnavailable"
      findings={findings}
      hint={<Trans>Off: the step is skipped with a warning.</Trans>}
      className="sm:col-span-2"
      value={step.ifUnavailable === "Fail"}
      onChange={(fail) => {
        onChange({ ifUnavailable: fail ? "Fail" : "Skip" });
      }}
    />
  );
}
