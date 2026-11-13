// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { SettingSecret } from "../parts/SettingSecret";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingText } from "../parts/SettingText";

import type { DeploymentForm } from "./deploymentSettings";

// The product key and the names Windows is registered to.
export function RegistrationGroup({
  form,
  canChange,
}: {
  form: DeploymentForm;
  canChange: boolean;
}) {
  return (
    <SettingsGroup title={<Trans>Registration</Trans>}>
      <div className="grid gap-4 md:grid-cols-3">
        <SettingSecret
          form={form}
          field="productKey"
          canChange={canChange}
          label={<Trans>Product key</Trans>}
          hint={<Trans>A ProductKey value overrides it.</Trans>}
        />
        <SettingText
          form={form}
          field="organization"
          canChange={canChange}
          label={<Trans>Organization</Trans>}
        />
        <SettingText form={form} field="owner" canChange={canChange} label={<Trans>Owner</Trans>} />
      </div>
    </SettingsGroup>
  );
}
