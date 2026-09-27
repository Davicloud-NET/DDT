// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQuery } from "@tanstack/react-query";

import { currentUserQuery, type CurrentUser } from "@/auth/auth";
import { Button } from "@/ui/Button";
import { Facts, Page, PageHeader, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { SecretValue } from "@/ui/SecretValue";
import { StateTag } from "@/ui/StateTag";

import { DirectorySettings, SectionErrors } from "./DirectorySettings";
import { RoleMapEditor } from "./RoleMapEditor";
import {
  LockNote,
  SettingLines,
  SettingSecret,
  SettingSelect,
  SettingSwitch,
  SettingText,
  SettingsGroup,
  SettingsSection,
} from "./SettingsParts";
import { redirectUri, testOidc, type OidcSettings, type OidcTestResult } from "./signIn";
import { useSettingsForm, type SettingsForm } from "./useSettingsForm";

// Administration > Sign-in: who besides the local accounts signs in, through a directory or a single sign-on
// provider, and which role they get there. Every field decides who reaches DDT, so only administrators read and change
// them; the server refuses everyone else, and the page asks nothing for them.
export function SignInSettingsPage() {
  const me = useQuery(currentUserQuery).data ?? null;

  if (me === null) {
    return null;
  }

  return (
    <Page className="max-w-[72rem]">
      <PageHeader title={<Trans>Sign-in</Trans>} />
      {me.roles.includes("Administrator") ? (
        <SignInSettings me={me} />
      ) : (
        <Notice>
          <Trans>
            Only administrators change how people sign in. Your own password and second factor are
            on the Account and security page in the account menu.
          </Trans>
        </Notice>
      )}
    </Page>
  );
}

function SignInSettings({ me }: { me: CurrentUser }) {
  return (
    <>
      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          Local accounts always sign in with their password. Here you let people sign in with their
          directory account or through a single sign-on provider as well, and decide which role
          their groups give them.
        </Trans>
      </p>
      <DirectorySettings me={me} />
      <SingleSignOnSettings />
    </>
  );
}

// The section oidc: a button on the sign-in page that signs people in at an OpenID Connect provider. A save rebuilds
// the sign-in scheme in the running server.
function SingleSignOnSettings() {
  const { t } = useLingui();
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

  const mapLock = form.lockOf("groupRoleMap");
  const mapped = Object.keys(values.groupRoleMap).length > 0;

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
                secret goes only to the provider it was entered for, so after a change of the
                address, enter it again.
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
        <ProviderTest authority={values.authority} />
      </SettingsGroup>

      <SettingsGroup title={<Trans>New accounts</Trans>}>
        <SettingSwitch
          form={form}
          field="autoProvision"
          canChange
          label={<Trans>Create an account for an identity DDT has not seen</Trans>}
          hint={<Trans>Off, an identity DDT has not seen is refused at the sign-in.</Trans>}
        />
        <SettingSelect
          form={form}
          field="autoProvisionRole"
          canChange
          label={<Trans>Role of a new account</Trans>}
          hint={
            mapped ? (
              <Trans>
                Not used while the claim map below has entries: the map decides the role.
              </Trans>
            ) : (
              <Trans>
                Operators can read the deployment passwords by deploying a machine they control.
              </Trans>
            )
          }
          options={[
            { id: "Viewer", label: t`Viewer` },
            { id: "Operator", label: t`Operator` },
          ]}
        />
      </SettingsGroup>

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
              Keycloak and Authentik send group names or paths, Entra ID the object IDs of the
              groups.
            </Trans>
          }
          placeholder="groups"
          mono
        />
        <RoleMapEditor
          label={t`Claim values and the roles they give`}
          map={values.groupRoleMap}
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
        <MapErrors form={form} />
      </SettingsGroup>

      <SectionErrors form={form} />
    </SettingsSection>
  );
}

function MapErrors({ form }: { form: SettingsForm<OidcSettings> }) {
  const errors = form.fieldErrors("groupRoleMap");

  return errors.length > 0 ? (
    <span className="type-small text-fail-text">{errors.join(" ")}</span>
  ) : null;
}

// Reads the provider's discovery document at the address in the form, before it is saved.
function ProviderTest({ authority }: { authority: string | null }) {
  const test = useMutation({
    mutationFn: (address: string) => testOidc(address),
  });

  return (
    <div className="flex flex-col gap-3">
      <span>
        <Button
          isDisabled={test.isPending || (authority ?? "").trim() === ""}
          onPress={() => {
            test.mutate((authority ?? "").trim());
          }}
        >
          <Trans>Test the provider</Trans>
        </Button>
      </span>
      {test.isError ? <Notice tone="fail">{test.error.message}</Notice> : null}
      {test.isSuccess ? <ProviderResult result={test.data} /> : null}
    </div>
  );
}

function ProviderResult({ result }: { result: OidcTestResult }) {
  return (
    <div className="flex flex-col gap-3 rounded-key bg-well p-3.5">
      <Facts
        items={[
          {
            label: <Trans>Provider</Trans>,
            value: (
              <StateTag tone={result.reached ? "ok" : "fail"}>
                {result.reached ? <Trans>Reached</Trans> : <Trans>Not reached</Trans>}
              </StateTag>
            ),
          },
          ...(result.issuer === null
            ? []
            : [{ label: <Trans>Issuer</Trans>, value: result.issuer, mono: true }]),
          { label: <Trans>Redirect URI</Trans>, value: result.redirectUri, mono: true },
        ]}
      />
      <p className="type-small text-ink">{result.message}</p>
    </div>
  );
}
