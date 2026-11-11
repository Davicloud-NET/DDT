// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";

import { SettingSelect } from "../parts/SettingSelect";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingText } from "../parts/SettingText";
import type { LdapForm, LdapSettings } from "../signIn";

import { PortField } from "./PortField";
import { TimeoutField } from "./TimeoutField";

export function ServerGroup({ form, values }: { form: LdapForm; values: LdapSettings }) {
  const { t } = useLingui();

  return (
    <SettingsGroup title={<Trans>Server</Trans>}>
      <div className="grid gap-4 md:grid-cols-2">
        <SettingText
          form={form}
          field="host"
          canChange
          label={<Trans>Directory server</Trans>}
          hint={<Trans>A domain controller or the domain's name.</Trans>}
          placeholder="dc1.corp.example"
          mono
        />
        <PortField form={form} />
        <SettingSelect
          form={form}
          field="transport"
          canChange
          label={<Trans>Encryption</Trans>}
          options={[
            { id: "Ldaps", label: t`LDAPS` },
            { id: "StartTls", label: t`StartTLS` },
            { id: "UnencryptedDangerous", label: t`Unencrypted (dangerous)` },
          ]}
        />
        <TimeoutField form={form} />
      </div>
      {values.transport === "UnencryptedDangerous" ? (
        <Notice tone="fail" title={<Trans>Passwords cross the network in clear text</Trans>}>
          <Trans>Use it only on a test network.</Trans>
        </Notice>
      ) : null}
    </SettingsGroup>
  );
}
