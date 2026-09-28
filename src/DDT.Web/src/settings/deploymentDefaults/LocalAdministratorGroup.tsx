// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { SettingSecret } from "../parts/SettingSecret";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingText } from "../parts/SettingText";

import type { DeploymentForm } from "./deploymentSettings";

export function LocalAdministratorGroup({
  form,
  canChange,
}: {
  form: DeploymentForm;
  canChange: boolean;
}) {
  return (
    <SettingsGroup title={<Trans>Local administrator</Trans>}>
      <p className="type-small text-ink-2">
        <Trans>
          A Write the answer file step with "Add the local administrator" creates this account.
          Operators can read the password by deploying a machine they control, so it should be one
          for deployed machines only.
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
          hint={<Trans>Without one, the step cannot add the account and the run fails.</Trans>}
        />
      </div>
    </SettingsGroup>
  );
}
