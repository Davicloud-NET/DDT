// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { FormCheckbox } from "../FormCheckbox";
import { FormField } from "../FormField";
import { fieldMessages } from "../problems";
import type { WriteUnattendStep } from "../sequences";
import { orNull, type KindFieldsProps } from "./kindFields";

import styles from "../form.module.scss";

const settings = [
  {
    field: "timeZone",
    label: "Time zone",
    hint: "A Windows time zone id as tzutil /l lists it, such as W. Europe Standard Time.",
  },
  { field: "locale", label: "Language and region", hint: "Such as de-DE." },
  {
    field: "keyboard",
    label: "Keyboard",
    hint: "An input locale such as de-DE or 0407:00000407.",
  },
] as const;

export function WriteUnattendFields({
  step,
  findings,
  onChange,
}: KindFieldsProps<WriteUnattendStep>) {
  return (
    <>
      <p className={styles.explain}>
        Writes the answer file Windows setup reads at its first start. An empty setting takes the
        server's default.
      </p>
      {settings.map((setting) => (
        <FormField
          key={setting.field}
          label={setting.label}
          messages={fieldMessages(findings, setting.field)}
          hint={setting.hint}
        >
          {(control) => (
            <input
              {...control}
              type="text"
              placeholder="Server default"
              value={step[setting.field] ?? ""}
              onChange={(event) => {
                onChange({ [setting.field]: orNull(event.target.value) });
              }}
            />
          )}
        </FormField>
      ))}
      <FormCheckbox
        label="Add the local administrator"
        checked={step.localAdministrator}
        messages={fieldMessages(findings, "localAdministrator")}
        hint="The account and its password are configured on the server, never in the sequence."
        onChange={(localAdministrator) => {
          onChange({ localAdministrator });
        }}
      />
    </>
  );
}
