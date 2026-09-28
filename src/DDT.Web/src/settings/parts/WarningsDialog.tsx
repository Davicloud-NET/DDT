// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";

import { settingsText } from "../settings";
import type { SettingsForm } from "../useSettingsForm";

// Warnings the server wants confirmed before it saves, such as a network wider than a /16.
export function WarningsDialog<T>({ form }: { form: SettingsForm<T> }) {
  const warnings = form.warnings ?? [];

  return (
    <Dialog
      isOpen={form.warnings !== null}
      onOpenChange={(open) => {
        if (!open) {
          form.cancelWarnings();
        }
      }}
      title={<Trans>Save anyway?</Trans>}
      isBusy={form.saving}
      footer={
        <>
          <Button onPress={form.cancelWarnings} isDisabled={form.saving}>
            <Trans>Change it</Trans>
          </Button>
          <Button variant="primary" onPress={form.confirmWarnings} isDisabled={form.saving}>
            <Trans>Save anyway</Trans>
          </Button>
        </>
      }
    >
      <ul className="flex list-disc flex-col gap-2 pl-5">
        {warnings.map((warning, index) => (
          <li key={index}>{settingsText(warning)}</li>
        ))}
      </ul>
    </Dialog>
  );
}
