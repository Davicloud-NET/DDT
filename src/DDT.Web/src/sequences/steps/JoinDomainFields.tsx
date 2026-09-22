// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { FormField } from "../FormField";
import { fieldMessages } from "../problems";
import type { JoinDomainStep } from "../sequences";
import { orNull, type KindFieldsProps } from "./kindFields";

import styles from "../form.module.scss";

function domainText(configured: boolean | null): string {
  switch (configured) {
    case true:
      return "Joins the domain configured on the server, in Windows after the hand-over. The machine needs a computer name.";
    case false:
      return "No domain is configured on the server, so this step cannot run.";
    case null:
      return "Joins the domain configured on the server, in Windows after the hand-over.";
  }
}

export function JoinDomainFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<JoinDomainStep>) {
  return (
    <>
      <p className={styles.explain}>{domainText(catalog.domainConfigured)}</p>
      <FormField
        label="Organizational unit"
        messages={fieldMessages(findings, "organizationalUnit")}
        hint="Such as OU=Workstations,DC=example,DC=com. Empty takes the configured default."
      >
        {(control) => (
          <input
            {...control}
            type="text"
            placeholder="Configured default"
            value={step.organizationalUnit ?? ""}
            onChange={(event) => {
              onChange({ organizationalUnit: orNull(event.target.value) });
            }}
          />
        )}
      </FormField>
    </>
  );
}
