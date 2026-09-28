// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { SettingSecret } from "../parts/SettingSecret";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingText } from "../parts/SettingText";
import type { LdapForm } from "../signIn";

export function BindAccountGroup({ form }: { form: LdapForm }) {
  return (
    <SettingsGroup title={<Trans>Bind account</Trans>}>
      <p className="type-small text-ink-2">
        <Trans>
          DDT reads the directory with this account: it finds the user who signs in, and their
          groups. A saved password goes only to the server it was entered for, so after a change of
          the server, port or encryption, enter it again.
        </Trans>
      </p>
      <div className="grid gap-4 md:grid-cols-2">
        <SettingText
          form={form}
          field="bindDn"
          canChange
          label={<Trans>Account</Trans>}
          placeholder="CN=ddt-bind,OU=Service accounts,DC=corp,DC=example"
          mono
        />
        <SettingSecret form={form} field="bindPassword" canChange label={<Trans>Password</Trans>} />
      </div>
    </SettingsGroup>
  );
}
