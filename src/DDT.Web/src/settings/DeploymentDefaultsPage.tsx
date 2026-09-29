// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Skeleton } from "@/ui/Skeleton";

import { ConsoleLogoPanel } from "./ConsoleLogoPanel";
import { ConsoleLanguageGroup } from "./deploymentDefaults/ConsoleLanguageGroup";
import type { DeploymentSettings } from "./deploymentDefaults/deploymentSettings";
import { DomainGroup } from "./deploymentDefaults/DomainGroup";
import { LocalAdministratorGroup } from "./deploymentDefaults/LocalAdministratorGroup";
import { WindowsSetupGroup } from "./deploymentDefaults/WindowsSetupGroup";
import { SettingsSection } from "./parts/SettingsSection";
import { useSettingsForm } from "./useSettingsForm";

export type { DeploymentSettings } from "./deploymentDefaults/deploymentSettings";

// The defaults every run starts from. A run keeps the values it started with. Operators can read them, and
// administrators can change them.
export function DeploymentDefaultsPage() {
  const form = useSettingsForm<DeploymentSettings>("deployment");
  const canChange = useIsAdministrator();

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
          <WindowsSetupGroup form={form} canChange={canChange} />
          <ConsoleLanguageGroup form={form} canChange={canChange} />
          <LocalAdministratorGroup form={form} canChange={canChange} />
          <DomainGroup form={form} stored={form.view.values} canChange={canChange} />
        </SettingsSection>
      )}
      <ConsoleLogoPanel canChange={canChange} />
    </Page>
  );
}
