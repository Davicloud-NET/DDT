// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { SecretValue } from "@/ui/SecretValue";

import { SettingLines } from "../parts/SettingLines";
import { SettingSecret } from "../parts/SettingSecret";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingText } from "../parts/SettingText";
import { redirectUri, type OidcForm } from "../signIn";

import { ProviderTest } from "./ProviderTest";

export function ProviderGroup({ form, authority }: { form: OidcForm; authority: string | null }) {
  return (
    <SettingsGroup title={<Trans>Provider</Trans>}>
      <div className="grid gap-4 md:grid-cols-2">
        <SettingText
          form={form}
          field="authority"
          canChange
          label={<Trans>Provider address</Trans>}
          hint={
            <Trans>
              The provider's https address, where DDT reads its discovery document. A saved client
              secret goes only to the provider it was entered for, so after a change of the address,
              enter it again.
            </Trans>
          }
          placeholder="https://login.example.com/realms/ddt"
          mono
        />
        <SettingText
          form={form}
          field="displayName"
          canChange
          label={<Trans>Button text</Trans>}
          hint={<Trans>What the button on the sign-in page says.</Trans>}
        />
        <SettingText
          form={form}
          field="clientId"
          canChange
          label={<Trans>Client ID</Trans>}
          hint={<Trans>The ID the provider shows for DDT's client.</Trans>}
          mono
        />
        <SettingSecret
          form={form}
          field="clientSecret"
          canChange
          label={<Trans>Client secret</Trans>}
        />
        <SettingLines
          form={form}
          field="scopes"
          canChange
          label={<Trans>Scopes</Trans>}
          hint={
            <Trans>
              One per line. openid is required; profile and email bring the name and the email
              address.
            </Trans>
          }
        />
        <div className="flex flex-col gap-1.5">
          <SecretValue label={<Trans>Redirect URI</Trans>} value={redirectUri()} />
          <span className="type-small text-muted">
            <Trans>
              Register this address as the redirect URI of DDT's client at the provider.
            </Trans>
          </span>
        </div>
      </div>
      <ProviderTest authority={authority} />
    </SettingsGroup>
  );
}
