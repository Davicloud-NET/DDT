// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";
import { Skeleton } from "@/ui/Skeleton";

import type { CertificateSettings } from "../certificate";
import { SettingLines } from "../parts/SettingLines";
import { SettingsSection } from "../parts/SettingsSection";
import { useSettingsForm } from "../useSettingsForm";

// The names the server is reached by. Generate issues a certificate for them, and an uploaded one must cover them.
export function ServerNames() {
  const form = useSettingsForm<CertificateSettings>("certificate");

  if (form.view === null) {
    return form.query.isError ? (
      <Notice tone="fail">
        <Trans>These settings could not be loaded.</Trans>
      </Notice>
    ) : (
      <Skeleton className="h-40 w-full" />
    );
  }

  return (
    <SettingsSection form={form} canChange title={<Trans>Server names</Trans>}>
      <SettingLines
        form={form}
        field="subjectAlternativeNames"
        canChange
        label={<Trans>Names and addresses</Trans>}
        hint={<Trans>One per line, such as ddt.corp.example or 10.0.0.5.</Trans>}
      />
    </SettingsSection>
  );
}
