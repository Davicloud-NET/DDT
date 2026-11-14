// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { FlagSetting } from "../fields/FlagSetting";
import { NumberSetting } from "../fields/NumberSetting";
import type { WindowsUpdateStep } from "../sequences";
import { ExcludedKbsSetting } from "./ExcludedKbsSetting";
import type { KindFieldsProps } from "./kindFields";

export function WindowsUpdateFields({
  step,
  findings,
  onChange,
}: KindFieldsProps<WindowsUpdateStep>) {
  return (
    <>
      <FlagSetting
        label={<Trans>Updates for other Microsoft products</Trans>}
        field="microsoftProducts"
        findings={findings}
        className="sm:col-span-2"
        value={step.microsoftProducts}
        onChange={(microsoftProducts) => {
          onChange({ microsoftProducts }, true);
        }}
      />
      <FlagSetting
        label={<Trans>Drivers</Trans>}
        field="drivers"
        findings={findings}
        className="sm:col-span-2"
        value={step.drivers}
        onChange={(drivers) => {
          onChange({ drivers }, true);
        }}
      />
      <FlagSetting
        label={<Trans>Optional updates and previews</Trans>}
        field="optionalUpdates"
        findings={findings}
        className="sm:col-span-2"
        value={step.optionalUpdates}
        onChange={(optionalUpdates) => {
          onChange({ optionalUpdates }, true);
        }}
      />
      <ExcludedKbsSetting
        findings={findings}
        value={step.excludedKbs}
        onChange={(excludedKbs) => {
          onChange({ excludedKbs });
        }}
      />
      <NumberSetting
        label={<Trans>Restarts at most</Trans>}
        field="restartLimit"
        findings={findings}
        hint={<Trans>From 1 to 20.</Trans>}
        minValue={1}
        maxValue={20}
        value={step.restartLimit}
        onChange={(restartLimit) => {
          onChange({ restartLimit });
        }}
      />
    </>
  );
}
