// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { TemplateField } from "../builder/TemplateField";
import { FlagSetting } from "../fields/FlagSetting";
import { NumberSetting } from "../fields/NumberSetting";
import type { PauseStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

export function PauseFields({ step, findings, onChange }: KindFieldsProps<PauseStep>) {
  const minutes = step.continueAfterMinutes;

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        <Trans>
          Waits until someone lets the run go on, at the machine or on its page. A restart in
          Windows PE before the disk is partitioned ends the run instead.
        </Trans>
      </p>
      <TemplateField
        label={<Trans>Message</Trans>}
        field="message"
        findings={findings}
        className="sm:col-span-2"
        hint={<Trans>Shown at the machine and on its page while the run waits.</Trans>}
        multiline
        mono={false}
        value={step.message}
        onChange={(message) => {
          onChange({ message });
        }}
      />
      <FlagSetting
        label={<Trans>Go on by itself after a while</Trans>}
        field="continueAfterMinutesSet"
        findings={findings}
        className="sm:col-span-2"
        hint={<Trans>Otherwise the run waits for as long as it takes.</Trans>}
        value={minutes !== null}
        onChange={(set) => {
          onChange({ continueAfterMinutes: set ? 30 : null }, true);
        }}
      />
      {minutes === null ? null : (
        <NumberSetting
          label={<Trans>Go on after this many minutes</Trans>}
          field="continueAfterMinutes"
          findings={findings}
          hint={<Trans>From 1 to 1440 minutes, a day.</Trans>}
          minValue={1}
          maxValue={1440}
          value={minutes}
          onChange={(continueAfterMinutes) => {
            onChange({ continueAfterMinutes });
          }}
        />
      )}
    </>
  );
}
