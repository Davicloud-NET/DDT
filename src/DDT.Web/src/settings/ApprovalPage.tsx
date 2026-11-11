// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Skeleton } from "@/ui/Skeleton";

import { SettingLines } from "./parts/SettingLines";
import { SettingNumber } from "./parts/SettingNumber";
import { SettingsGroup } from "./parts/SettingsGroup";
import { SettingsSection } from "./parts/SettingsSection";
import { SettingSwitch } from "./parts/SettingSwitch";
import { useSettingsForm } from "./useSettingsForm";

export interface MachineSettings {
  requireWebApproval: boolean;
  maxWaitingPerAddress: number;
  maxWaiting: number;
  zeroTouchNetworks: string[];
}

// Who lets a netbooting machine deploy: someone who signs in at the machine, an operator on the web, or nobody on the
// networks listed for zero touch. Approved machines aren't checked again. Operators can read these settings.
export function ApprovalPage() {
  const form = useSettingsForm<MachineSettings>("machines");
  const canChange = useIsAdministrator();

  return (
    <Page className="max-w-[72rem]">
      <PageHeader title={<Trans>Approval and zero touch</Trans>} />
      {form.view === null ? (
        <Skeleton className="h-64 w-full" />
      ) : (
        <SettingsSection
          form={form}
          canChange={canChange}
          title={<Trans>Authorizing machines</Trans>}
        >
          <SettingSwitch
            form={form}
            field="requireWebApproval"
            canChange={canChange}
            label={<Trans>Also approve every machine on the web</Trans>}
            hint={
              <Trans>
                A sign-in at the machine then only records who is there. Zero touch is off while
                this is on.
              </Trans>
            }
          />
          <SettingLines
            form={form}
            field="zeroTouchNetworks"
            canChange={canChange}
            label={<Trans>Zero touch networks</Trans>}
            hint={
              <Trans>
                One network per line, such as 10.20.0.0/24. Machines there deploy without approval
                and receive the deployment passwords. List only networks where every machine may be
                wiped, and never one a proxy forwards from.
              </Trans>
            }
          />
          <SettingsGroup title={<Trans>Waiting machines</Trans>}>
            <p className="type-small text-ink-2">
              <Trans>A machine over either limit is refused.</Trans>
            </p>
            <div className="flex flex-wrap gap-4">
              <SettingNumber
                form={form}
                field="maxWaitingPerAddress"
                canChange={canChange}
                label={<Trans>Waiting from one address</Trans>}
                minValue={1}
              />
              <SettingNumber
                form={form}
                field="maxWaiting"
                canChange={canChange}
                label={<Trans>Waiting in all</Trans>}
                minValue={1}
              />
            </div>
          </SettingsGroup>
        </SettingsSection>
      )}
    </Page>
  );
}
