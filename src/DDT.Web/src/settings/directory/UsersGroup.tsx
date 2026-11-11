// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingText } from "../parts/SettingText";
import type { LdapForm } from "../signIn";

// Braces in a Lingui message mark its arguments, so the user filter's {0} placeholder goes into the text as a value.
const USER_TOKEN = "{0}";

export function UsersGroup({ form }: { form: LdapForm }) {
  return (
    <SettingsGroup title={<Trans>Users</Trans>}>
      <div className="grid gap-4 md:grid-cols-2">
        <SettingText
          form={form}
          field="baseDn"
          canChange
          label={<Trans>Search base</Trans>}
          placeholder="DC=corp,DC=example"
          mono
        />
        <SettingText
          form={form}
          field="userFilter"
          canChange
          label={<Trans>User filter</Trans>}
          hint={
            <Trans>
              Must contain {USER_TOKEN}, which DDT replaces with the user name typed at sign-in.
            </Trans>
          }
          placeholder="(&(objectClass=user)(sAMAccountName={0}))"
          mono
        />
        <SettingText
          form={form}
          field="immutableIdAttribute"
          canChange
          label={<Trans>Attribute that identifies an account</Trans>}
          hint={
            <Trans>
              With another attribute, every account is taken for a new one at its next sign-in.
            </Trans>
          }
          placeholder="objectGUID"
          mono
        />
        <SettingText
          form={form}
          field="displayNameAttribute"
          canChange
          label={<Trans>Name attribute</Trans>}
          placeholder="displayName"
          mono
        />
        <SettingText
          form={form}
          field="emailAttribute"
          canChange
          label={<Trans>Email attribute</Trans>}
          placeholder="mail"
          mono
        />
      </div>
    </SettingsGroup>
  );
}
