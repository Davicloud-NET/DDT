// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { SettingSelect } from "../parts/SettingSelect";
import { SettingsGroup } from "../parts/SettingsGroup";

import type { DeploymentForm } from "./deploymentSettings";

// The language names stay untranslated, as each language calls itself.
export function ConsoleLanguageGroup({
  form,
  canChange,
}: {
  form: DeploymentForm;
  canChange: boolean;
}) {
  return (
    <SettingsGroup title={<Trans>The console at the machine</Trans>}>
      <SettingSelect
        form={form}
        field="consoleLanguage"
        canChange={canChange}
        label={<Trans>Language</Trans>}
        hint={
          <Trans>
            The language the console starts in, in Windows PE and in the installed Windows. Someone
            at the machine can switch with F5.
          </Trans>
        }
        empty={t`The language of Windows PE`}
        options={[
          { id: "en", label: "English" },
          { id: "de", label: "Deutsch" },
        ]}
      />
    </SettingsGroup>
  );
}
