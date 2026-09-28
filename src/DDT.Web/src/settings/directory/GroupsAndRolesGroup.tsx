// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { FieldErrorText } from "@/ui/FieldErrorText";

import { LockNote } from "../parts/LockNote";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingSwitch } from "../parts/SettingSwitch";
import { RoleMapEditor } from "../roleMap/RoleMapEditor";
import { connectionChanged, type LdapForm, type LdapSettings } from "../signIn";

import { GroupFinder } from "./GroupFinder";
import { GroupName } from "./GroupName";
import type { DirectoryGroupNames } from "./useDirectoryGroupNames";

export function GroupsAndRolesGroup({
  form,
  stored,
  values,
  names,
}: {
  form: LdapForm;
  stored: LdapSettings;
  values: LdapSettings;
  names: DirectoryGroupNames;
}) {
  const { t } = useLingui();
  const mapLock = form.lockOf("groupRoleMap");
  const map = values.groupRoleMap;

  return (
    <SettingsGroup title={<Trans>Groups and roles</Trans>}>
      <p className="max-w-[80ch] type-small text-ink-2">
        <Trans>
          At each sign-in, a directory account gets the highest role its groups below give it, and
          an account in none of them cannot sign in. While no group is listed, administrators choose
          the role of each directory account on the Users and roles page.
        </Trans>
      </p>
      <SettingSwitch
        form={form}
        field="resolveNestedGroups"
        canChange
        label={<Trans>Read the account's groups, nested groups included</Trans>}
        hint={<Trans>The group map needs it: while it is off, DDT reads no groups at all.</Trans>}
      />
      <RoleMapEditor
        label={t`Groups and the roles they give`}
        map={map}
        onChange={(next) => {
          form.change("groupRoleMap", next);
        }}
        canChange={mapLock === null}
        errorsOf={(key) => form.fieldErrors(`groupRoleMap[${key}]`)}
        describe={(key) => <GroupName description={names.describe(key)} />}
        empty={<Trans>No group is listed, so groups decide no role.</Trans>}
        addLabel={<Trans>Add a group by its distinguished name</Trans>}
        addHint={<Trans>A group added here gives Viewer until you choose its role.</Trans>}
        addPlaceholder="CN=DDT Admins,OU=Groups,DC=corp,DC=example"
        addAction={<Trans>Add group</Trans>}
        duplicate={t`This group is listed already.`}
      />
      {mapLock !== null ? <LockNote lock={mapLock} /> : null}
      <FieldErrorText errors={form.fieldErrors("groupRoleMap")} />
      {mapLock === null ? (
        <GroupFinder
          ready={names.storedReady}
          unsaved={connectionChanged(stored, values, form.secrets)}
          map={map}
          onAdd={(group, name) => {
            names.remember(group, name);
            form.change("groupRoleMap", { ...map, [group]: "Viewer" });
          }}
        />
      ) : null}
    </SettingsGroup>
  );
}
