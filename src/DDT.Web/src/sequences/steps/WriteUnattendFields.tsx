// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { TemplateField } from "../builder/TemplateField";
import { FlagSetting } from "../fields/FlagSetting";
import type { WriteUnattendStep } from "../sequences";
import { orNull, type KindFieldsProps } from "./kindFields";

export function WriteUnattendFields({
  step,
  findings,
  onChange,
}: KindFieldsProps<WriteUnattendStep>) {
  const serverDefault = t`Server default`;

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        <Trans>
          Writes the answer file Windows setup reads at its first start. An empty setting takes the
          server's default.
        </Trans>
      </p>
      <TemplateField
        label={<Trans>Time zone</Trans>}
        field="timeZone"
        mono={false}
        howTo={false}
        findings={findings}
        className="sm:col-span-2"
        hint={
          <Trans>
            A Windows time zone id as tzutil /l lists it, such as W. Europe Standard Time.
          </Trans>
        }
        placeholder={serverDefault}
        value={step.timeZone ?? ""}
        onChange={(text) => {
          onChange({ timeZone: orNull(text) });
        }}
      />
      <TemplateField
        label={<Trans>Language and region</Trans>}
        field="locale"
        findings={findings}
        hint={<Trans>Such as de-DE.</Trans>}
        placeholder={serverDefault}
        howTo={false}
        value={step.locale ?? ""}
        onChange={(text) => {
          onChange({ locale: orNull(text) });
        }}
      />
      <TemplateField
        label={<Trans>Keyboard</Trans>}
        field="keyboard"
        findings={findings}
        hint={<Trans>An input locale such as de-DE or 0407:00000407.</Trans>}
        placeholder={serverDefault}
        howTo={false}
        value={step.keyboard ?? ""}
        onChange={(text) => {
          onChange({ keyboard: orNull(text) });
        }}
      />
      <FlagSetting
        label={<Trans>Add the local administrator</Trans>}
        field="localAdministrator"
        findings={findings}
        className="sm:col-span-2"
        hint={
          <Trans>
            The account and its password are set on the Deployment defaults page, never in the
            sequence.
          </Trans>
        }
        value={step.localAdministrator}
        onChange={(localAdministrator) => {
          onChange({ localAdministrator });
        }}
      />
    </>
  );
}
