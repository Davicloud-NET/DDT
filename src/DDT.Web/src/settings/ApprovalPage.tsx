// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Page, PageHeader, Skeleton } from "@/ui/Layout";

import {
  SettingLines,
  SettingNumber,
  SettingSwitch,
  SettingsGroup,
  SettingsSection,
} from "./SettingsParts";
import { useCanChangeSettings, useSettingsForm } from "./useSettingsForm";

export interface MachineSettings {
  requireWebApproval: boolean;
  maxWaitingPerAddress: number;
  maxWaiting: number;
  zeroTouchNetworks: string[];
}

// Who lets a netbooting machine deploy: someone who signs in at it, an operator on the web, or, on the networks listed
// for zero touch, nobody. Machines already approved are not judged again. Operators read these settings.
export function ApprovalPage() {
  const form = useSettingsForm<MachineSettings>("machines");
  const canChange = useCanChangeSettings();

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
          description={
            <Trans>
              By default, a person who signs in at a netbooting machine authorizes it for the
              sequence they choose, and an operator can approve a waiting machine on the web. Zero
              touch skips both on the networks listed here.
            </Trans>
          }
        >
          <SettingSwitch
            form={form}
            field="requireWebApproval"
            canChange={canChange}
            label={<Trans>Also approve every machine on the web</Trans>}
            hint={
              <Trans>
                For places that need two people: a sign-in at the machine then only records who is
                there, and an operator approves it on the web. Zero touch is off while this is on.
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
                One network per line, such as 10.20.0.0/24. A machine that netboots from one of them
                deploys with the sequence a rule chooses, without anybody signing in or approving
                it, and receives the deployment passwords. List only networks where every machine
                may be wiped, and never a network a proxy forwards from.
              </Trans>
            }
          />
          <SettingsGroup title={<Trans>Waiting machines</Trans>}>
            <p className="type-small text-ink-2">
              <Trans>
                Anyone who reaches the server can register a machine, so the number waiting for
                approval is capped, for each address and in all. A machine over the cap is refused
                until others are approved or removed.
              </Trans>
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
