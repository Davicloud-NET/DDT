// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";
import { Skeleton } from "@/ui/Skeleton";

import { SettingSecret } from "../parts/SettingSecret";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingsSection } from "../parts/SettingsSection";
import { SettingText } from "../parts/SettingText";
import { useSettingsForm } from "../useSettingsForm";

export interface AutopilotSettings {
  tenantId: string;
  clientId: string;
  defaultGroupTag: string;
}

// The autopilot section. Its secret is clientSecret, which a change of tenant or client ID asks for again.
export function AutopilotSettingsForm() {
  const form = useSettingsForm<AutopilotSettings>("autopilot");

  if (form.view === null || form.values === null) {
    return form.query.isError ? (
      <Notice tone="fail">
        <Trans>These settings could not be loaded.</Trans>
      </Notice>
    ) : (
      <Skeleton className="h-64 w-full" />
    );
  }

  return (
    <SettingsSection
      form={form}
      canChange
      title={<Trans>Upload to Autopilot</Trans>}
      description={
        <Trans>
          An app registration with the Microsoft Graph application permission
          DeviceManagementServiceConfig.ReadWrite.All. Without it, download the CSV on a machine's
          page.
        </Trans>
      }
    >
      <SettingsGroup title={<Trans>App registration</Trans>}>
        <div className="grid gap-4 md:grid-cols-2">
          <SettingText
            form={form}
            field="tenantId"
            canChange
            label={<Trans>Tenant</Trans>}
            hint={
              <Trans>Tenant ID or domain. After a change, enter the client secret again.</Trans>
            }
            placeholder="contoso.onmicrosoft.com"
            mono
          />
          <SettingText
            form={form}
            field="clientId"
            canChange
            label={<Trans>Client ID</Trans>}
            mono
          />
          <SettingSecret
            form={form}
            field="clientSecret"
            canChange
            label={<Trans>Client secret</Trans>}
          />
        </div>
      </SettingsGroup>
      <SettingText
        form={form}
        field="defaultGroupTag"
        canChange
        label={<Trans>Default group tag</Trans>}
        hint={<Trans>For steps that set none.</Trans>}
      />
    </SettingsSection>
  );
}
