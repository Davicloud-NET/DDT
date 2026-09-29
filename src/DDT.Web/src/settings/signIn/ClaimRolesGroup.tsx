// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { FieldErrorText } from "@/ui/FieldErrorText";

import { LockNote } from "../parts/LockNote";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingText } from "../parts/SettingText";
import { RoleMapEditor } from "../roleMap/RoleMapEditor";
import type { OidcForm } from "../signIn";

// The claim that carries the groups, and the map from its values to roles.
export function ClaimRolesGroup({ form, map }: { form: OidcForm; map: Record<string, string> }) {
  const { t } = useLingui();
  const mapLock = form.lockOf("groupRoleMap");

  return (
    <SettingsGroup title={<Trans>Groups and roles</Trans>}>
      <p className="max-w-[80ch] type-small text-ink-2">
        <Trans>
          While the map has entries, an account single sign-on created gets the highest role its
          groups give it at each sign-in, and an identity in none of them cannot sign in. A local
          account linked to an identity keeps the role an administrator gave it.
        </Trans>
      </p>
      <SettingText
        form={form}
        field="groupsClaim"
        canChange
        label={<Trans>Claim with the groups</Trans>}
        hint={
          <Trans>
            Keycloak and Authentik send group names or paths, Entra ID the object IDs of the groups.
          </Trans>
        }
        placeholder="groups"
        mono
      />
      <RoleMapEditor
        label={t`Claim values and the roles they give`}
        map={map}
        onChange={(next) => {
          form.change("groupRoleMap", next);
        }}
        canChange={mapLock === null}
        errorsOf={(key) => form.fieldErrors(`groupRoleMap[${key}]`)}
        empty={
          <Trans>
            No claim value is listed, so the groups decide no role: a new account gets the role
            above, and administrators change it on the Users and roles page.
          </Trans>
        }
        addLabel={<Trans>Add a claim value</Trans>}
        addHint={
          <Trans>
            A group as the provider sends it in the claim above. It gives Viewer until you choose
            its role.
          </Trans>
        }
        addPlaceholder="ddt-admins"
        addAction={<Trans>Add value</Trans>}
        duplicate={t`This value is listed already.`}
      />
      {mapLock !== null ? <LockNote lock={mapLock} /> : null}
      <FieldErrorText errors={form.fieldErrors("groupRoleMap")} />
    </SettingsGroup>
  );
}
