// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { SettingSelect } from "../parts/SettingSelect";
import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingSwitch } from "../parts/SettingSwitch";
import type { OidcForm } from "../signIn";

// mapped says whether the claim map has entries. If it has, they decide the role instead of autoProvisionRole.
export function NewAccountsGroup({ form, mapped }: { form: OidcForm; mapped: boolean }) {
  const { t } = useLingui();

  return (
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
            <Trans>Not used while the claim map below has entries: the map decides the role.</Trans>
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
  );
}
