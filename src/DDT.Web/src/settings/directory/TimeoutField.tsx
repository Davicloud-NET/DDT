// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { NumberField } from "@/ui/NumberField";

import { FieldFrame } from "../parts/FieldFrame";
import { fieldProps } from "../parts/fieldProps";
import { secondsOf, spanOf, type LdapForm } from "../signIn";

// The timeout is a duration on the server and whole seconds here, so it is not a SettingNumber.
export function TimeoutField({ form }: { form: LdapForm }) {
  const seconds = secondsOf(form.values?.timeout ?? null);

  return (
    <FieldFrame form={form} field="timeout">
      <NumberField
        label={<Trans>Timeout in seconds</Trans>}
        hint={<Trans>How long DDT waits for each answer of the directory.</Trans>}
        minValue={1}
        formatOptions={{ useGrouping: false }}
        value={seconds ?? Number.NaN}
        onChange={(next) => {
          if (!Number.isNaN(next)) {
            form.change("timeout", spanOf(next));
          }
        }}
        {...fieldProps(form, "timeout", true)}
        className="max-w-60"
      />
    </FieldFrame>
  );
}
