// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";
import { Skeleton } from "@/ui/Skeleton";

import { LevelRows } from "./logging/LevelRows";
import type { LoggingSettings } from "./logging/logLevels";
import { SettingsSection } from "./parts/SettingsSection";
import { useSettingsForm } from "./useSettingsForm";

// How much the server writes to its log, per category. A change applies at once, without a restart.
export function LoggingPanel() {
  const form = useSettingsForm<LoggingSettings>("logging");

  if (form.view === null || form.values === null) {
    return form.query.isError ? (
      <Notice tone="fail">
        <Trans>These settings could not be loaded.</Trans>
      </Notice>
    ) : (
      <Skeleton className="h-64 w-full" />
    );
  }

  return (
    <SettingsSection
      form={form}
      canChange
      title={<Trans>Log levels</Trans>}
      description={
        <Trans>
          How much the server writes to its log, per category. A category covers the categories
          below it, so DDT.Pxe covers DDT.Pxe.Tftp as well, and Default applies to every category
          not listed. A change applies at once.
        </Trans>
      }
    >
      <LevelRows form={form} logLevel={form.values.logLevel} />
      <p className="max-w-[80ch] type-small text-ink-2">
        <Trans>
          When a machine does not netboot, set DDT.Pxe to Debug or Trace and let the machine try
          again: the log then says what it asked for and what the server answered. Set it back
          afterwards, since Trace writes a line for every packet.
        </Trans>
      </p>
    </SettingsSection>
  );
}
