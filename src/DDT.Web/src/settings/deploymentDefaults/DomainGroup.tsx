// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { DomainJoinCheck } from "@/sequences/steps/DomainJoinCheck";

import { SettingSecret } from "../parts/SettingSecret";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingText } from "../parts/SettingText";
import { valueAt } from "../valuePath";

import type { DeploymentForm, DeploymentSettings } from "./deploymentSettings";

// The domain a Join the domain step joins. The server's check uses the saved settings, so it names the saved
// organizational unit, not the one being typed.
export function DomainGroup({
  form,
  stored,
  canChange,
}: {
  form: DeploymentForm;
  stored: DeploymentSettings;
  canChange: boolean;
}) {
  return (
    <SettingsGroup title={<Trans>Domain</Trans>}>
      <p className="type-small text-ink-2">
        <Trans>
          A Join the domain step joins the machine online in Windows with this account, which it
          fetches while the step runs. Left without a domain name, machines stay in a workgroup.
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
          hint={<Trans>Where new computer accounts go. Left empty, the domain's default.</Trans>}
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
            (valueAt(stored, "domain.organizationalUnit") as string | null) ?? null
          }
        />
      ) : null}
    </SettingsGroup>
  );
}
