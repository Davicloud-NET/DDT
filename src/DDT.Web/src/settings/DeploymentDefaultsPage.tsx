// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { DomainJoinCheck } from "@/sequences/steps/DomainJoinCheck";
import { Page, PageHeader, Skeleton } from "@/ui/Layout";

import {
  SettingSecret,
  SettingSelect,
  SettingText,
  SettingsGroup,
  SettingsSection,
} from "./SettingsParts";
import { ConsoleLogoPanel } from "./ConsoleLogoPanel";
import { useCanChangeSettings, useSettingsForm, valueAt } from "./useSettingsForm";

export interface DeploymentSettings {
  timeZone: string | null;
  locale: string | null;
  keyboard: string | null;
  consoleLanguage: string | null;
  localAdministrator: { name: string };
  domain: {
    name: string | null;
    organizationalUnit: string | null;
    userName: string | null;
    controller: string | null;
  };
}

// The defaults every run starts from: the answer file's regional settings, the local administrator, and the domain
// a Join the domain step joins. A task sequence step can override the regional settings and the organizational unit;
// a run keeps the values it started with. Operators read them; administrators change them.
export function DeploymentDefaultsPage() {
  const form = useSettingsForm<DeploymentSettings>("deployment");
  const canChange = useCanChangeSettings();

  return (
    <Page className="max-w-[72rem]">
      <PageHeader title={<Trans>Deployment defaults</Trans>} />
      {form.view === null ? (
        <Skeleton className="h-64 w-full" />
      ) : (
        <SettingsSection
          form={form}
          canChange={canChange}
          title={<Trans>Defaults for every run</Trans>}
          description={
            <Trans>
              A change applies to runs that start after it. A Write the answer file step can set its
              own time zone, language and keyboard, and a Join the domain step its own
              organizational unit.
            </Trans>
          }
        >
          <SettingsGroup title={<Trans>Windows setup</Trans>}>
            <div className="grid gap-4 md:grid-cols-3">
              <SettingText
                form={form}
                field="timeZone"
                canChange={canChange}
                label={<Trans>Time zone</Trans>}
                hint={
                  <Trans>
                    A Windows time zone id. Left empty, Windows picks one from the language.
                  </Trans>
                }
                placeholder="W. Europe Standard Time"
              />
              <SettingText
                form={form}
                field="locale"
                canChange={canChange}
                label={<Trans>Language and region</Trans>}
                hint={<Trans>A culture name. Left empty, the image's language.</Trans>}
                placeholder="de-DE"
                mono
              />
              <SettingText
                form={form}
                field="keyboard"
                canChange={canChange}
                label={<Trans>Keyboard</Trans>}
                hint={<Trans>An input locale. Left empty, the language's.</Trans>}
                placeholder="0407:00000407"
                mono
              />
            </div>
          </SettingsGroup>

          <SettingsGroup title={<Trans>The console at the machine</Trans>}>
            <SettingSelect
              form={form}
              field="consoleLanguage"
              canChange={canChange}
              label={<Trans>Language</Trans>}
              hint={
                <Trans>
                  The language the console starts in, in Windows PE and in the installed Windows.
                  Someone at the machine can switch with F5.
                </Trans>
              }
              empty={t`The language of Windows PE`}
              options={[
                { id: "en", label: "English" },
                { id: "de", label: "Deutsch" },
              ]}
            />
          </SettingsGroup>

          <SettingsGroup title={<Trans>Local administrator</Trans>}>
            <p className="type-small text-ink-2">
              <Trans>
                A Write the answer file step with "Add the local administrator" creates this
                account. Operators can read the password by deploying a machine they control, so it
                should be one for deployed machines only.
              </Trans>
            </p>
            <div className="grid gap-4 md:grid-cols-2">
              <SettingText
                form={form}
                field="localAdministrator.name"
                canChange={canChange}
                label={<Trans>Name</Trans>}
                mono
              />
              <SettingSecret
                form={form}
                field="localAdministrator.password"
                canChange={canChange}
                label={<Trans>Password</Trans>}
                hint={
                  <Trans>Without one, the step cannot add the account and the run fails.</Trans>
                }
              />
            </div>
          </SettingsGroup>

          <SettingsGroup title={<Trans>Domain</Trans>}>
            <p className="type-small text-ink-2">
              <Trans>
                A Join the domain step joins the machine online in Windows with this account, which
                it fetches while the step runs. Left without a domain name, machines stay in a
                workgroup.
              </Trans>
            </p>
            <div className="grid gap-4 md:grid-cols-2">
              <SettingText
                form={form}
                field="domain.name"
                canChange={canChange}
                label={<Trans>Domain</Trans>}
                placeholder="corp.example"
                mono
              />
              <SettingText
                form={form}
                field="domain.organizationalUnit"
                canChange={canChange}
                label={<Trans>Organizational unit</Trans>}
                hint={
                  <Trans>Where new computer accounts go. Left empty, the domain's default.</Trans>
                }
                placeholder="OU=Workstations,DC=corp,DC=example"
                mono
              />
              <SettingText
                form={form}
                field="domain.userName"
                canChange={canChange}
                label={<Trans>Join account</Trans>}
                placeholder="CORP\join"
                mono
              />
              <SettingSecret
                form={form}
                field="domain.password"
                canChange={canChange}
                label={<Trans>Join account password</Trans>}
              />
              <SettingText
                form={form}
                field="domain.controller"
                canChange={canChange}
                label={<Trans>Domain controller for the check</Trans>}
                hint={<Trans>Only the check below uses it. Left empty, the domain's name.</Trans>}
                mono
              />
            </div>
            {canChange ? (
              <DomainJoinCheck
                organizationalUnit={
                  (valueAt(form.view.values, "domain.organizationalUnit") as string | null) ?? null
                }
              />
            ) : null}
          </SettingsGroup>
        </SettingsSection>
      )}
      <ConsoleLogoPanel canChange={canChange} />
    </Page>
  );
}
