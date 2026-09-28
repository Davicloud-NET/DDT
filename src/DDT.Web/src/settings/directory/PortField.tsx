// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { NumberField } from "@/ui/NumberField";

import { FieldFrame } from "../parts/FieldFrame";
import { fieldProps } from "../parts/fieldProps";
import type { LdapForm } from "../signIn";

// Not a SettingNumber: a port reads better without digit grouping, as in 3269 for the global catalog, and an
// emptied field keeps the stored port instead of storing null.
export function PortField({ form }: { form: LdapForm }) {
  const port = form.values?.port;

  return (
    <FieldFrame form={form} field="port">
      <NumberField
        label={<Trans>Port</Trans>}
        hint={<Trans>LDAPS uses 636, StartTLS and unencrypted 389.</Trans>}
        minValue={1}
        maxValue={65535}
        formatOptions={{ useGrouping: false }}
        value={typeof port === "number" ? port : Number.NaN}
        onChange={(next) => {
          if (!Number.isNaN(next)) {
            form.change("port", next);
          }
        }}
        {...fieldProps(form, "port", true)}
        className="max-w-60"
      />
    </FieldFrame>
  );
}
