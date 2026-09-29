// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { useBuilder } from "../builder/builderData";
import { TemplateField } from "../builder/TemplateField";
import { ChoiceSetting } from "../fields/ChoiceSetting";
import type { SetVariableStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";

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
