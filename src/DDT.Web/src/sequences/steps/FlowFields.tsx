// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { useBuilder } from "../builder/builderData";
import { TemplateField } from "../builder/TemplateField";
import { ChoiceSetting, FlagSetting, NumberSetting } from "../fields";
import type { PauseStep, SetVariableStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

// The leaves of the flow that work on the run itself: setting a variable, and waiting for someone.

export function SetVariableFields({ step, findings, onChange }: KindFieldsProps<SetVariableStep>) {
  const { variables } = useBuilder();
  const settable = variables.filter((variable) => variable.setBySteps);
  const current = step.variable;
  const listed = current === "" || settable.some((variable) => variable.name === current);

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        <Trans>
          Sets a variable of this sequence while the run goes on. Steps after it, templates and
          conditions read the new value.
        </Trans>
      </p>
      <ChoiceSetting
        label={<Trans>Variable</Trans>}
        field="variable"
        findings={findings}
        className="sm:col-span-2"
        hint={
          settable.length === 0 ? (
            <Trans>
              No variable of this sequence may be set by steps yet. Declare one on the Variables
              tab.
            </Trans>
          ) : (
            <Trans>Only the variables that steps may set are offered.</Trans>
          )
        }
        placeholder={t`Choose a variable`}
        value={current === "" ? null : current}
        choices={[
          ...(listed ? [] : [{ id: current, label: t`${current}, which steps may not set` }]),
          ...settable.map((variable) => ({
            id: variable.name,
            label: variable.name,
            ...(variable.description === null ? {} : { description: variable.description }),
          })),
        ]}
        onChange={(variable) => {
          onChange({ variable }, true);
        }}
      />
      <TemplateField
        label={<Trans>Value</Trans>}
        field="value"
        findings={findings}
        className="sm:col-span-2"
        value={step.value}
        onChange={(value) => {
          onChange({ value });
        }}
      />
    </>
  );
}

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
