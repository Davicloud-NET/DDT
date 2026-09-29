// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingText } from "../parts/SettingText";

import type { DeploymentForm } from "./deploymentSettings";

// The answer file's regional settings.
export function WindowsSetupGroup({
  form,
  canChange,
}: {
  form: DeploymentForm;
  canChange: boolean;
}) {
  return (
    <SettingsGroup title={<Trans>Windows setup</Trans>}>
      <div className="grid gap-4 md:grid-cols-3">
        <SettingText
          form={form}
          field="timeZone"
          canChange={canChange}
          label={<Trans>Time zone</Trans>}
          hint={
            <Trans>A Windows time zone id. Left empty, Windows picks one from the language.</Trans>
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
  );
}
