// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";
import { Skeleton } from "@/ui/Skeleton";

import { SettingsSection } from "../parts/SettingsSection";
import { SettingSwitch } from "../parts/SettingSwitch";
import type { OidcSettings } from "../signIn";
import { useSettingsForm } from "../useSettingsForm";

import { ClaimRolesGroup } from "./ClaimRolesGroup";
import { NewAccountsGroup } from "./NewAccountsGroup";
import { ProviderGroup } from "./ProviderGroup";

// The oidc section: a button on the sign-in page that signs people in at an OpenID Connect provider. A save rebuilds
// the sign-in scheme in the running server.
export function SingleSignOnSettings() {
  const form = useSettingsForm<OidcSettings>("oidc");
  const view = form.view;
  const values = form.values;

  if (view === null || values === null) {
    return form.query.isError ? (
      <Notice tone="fail" title={<Trans>The single sign-on settings could not be loaded.</Trans>}>
        {form.query.error.message}
      </Notice>
    ) : (
      <Skeleton className="h-64 w-full" />
    );
  }

  return (
    <SettingsSection
      form={form}
      canChange
      title={<Trans>Single sign-on</Trans>}
      description={
        <Trans>
          The sign-in page offers a button that signs people in at an OpenID Connect provider, such
          as Entra ID, Keycloak or Authentik. DDT never links an identity to an existing account by
          its email address.
        </Trans>
      }
    >
      <SettingSwitch
        form={form}
        field="enabled"
        canChange
        label={<Trans>Offer single sign-on on the sign-in page</Trans>}
      />
      <ProviderGroup form={form} authority={values.authority} />
      <NewAccountsGroup form={form} mapped={Object.keys(values.groupRoleMap).length > 0} />
      <ClaimRolesGroup form={form} map={values.groupRoleMap} />
    </SettingsSection>
  );
}
