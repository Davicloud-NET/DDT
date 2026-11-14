// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { ConfirmDialog } from "@/ui/ConfirmDialog";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";

import type { RecoveryKeyView } from "./machineRecords";
import { RecoveryKeyRow } from "./RecoveryKeyRow";
import { useRecoveryKeyActions } from "./useRecoveryKeyActions";

interface RecoveryKeysPanelProps {
  machineId: string;
  keys: RecoveryKeyView[];
  now: number;
}

// The BitLocker recovery keys the machine's runs handed over, newest first. Everyone sees they exist. Only an
// administrator reveals one, or deletes one that was replaced.
export function RecoveryKeysPanel({ machineId, keys, now }: RecoveryKeysPanelProps) {
  const { t } = useLingui();
  const isAdministrator = useIsAdministrator();
  const actions = useRecoveryKeyActions(machineId);
  const deleting = keys.find((key) => key.id === actions.deleteOn) ?? null;
  const deletingId = deleting?.protectorId.toUpperCase() ?? "";

  return (
    <Panel title={<Trans>Recovery keys</Trans>}>
      {actions.reveal.isError ? <Notice tone="fail">{actions.reveal.error.message}</Notice> : null}
      <ul aria-label={t`Recovery keys`} className="-mt-1 flex flex-col">
        {keys.map((key) => (
          <RecoveryKeyRow
            key={key.id}
            recoveryKey={key}
            actions={isAdministrator ? actions : null}
            now={now}
          />
        ))}
      </ul>

      <ConfirmDialog
        isOpen={deleting !== null}
        onOpenChange={(open) => {
          if (!open) {
            actions.setDeleteOn(null);
          }
        }}
        title={<Trans>Delete the key {deletingId}?</Trans>}
        confirmLabel={<Trans>Delete</Trans>}
        danger
        isBusy={actions.remove.isPending}
        error={actions.remove.isError ? actions.remove.error.message : undefined}
        onConfirm={() => {
          if (deleting !== null) {
            actions.remove.mutate(deleting.id);
          }
        }}
      >
        <p>
          <Trans>DDT no longer holds this replaced key. This cannot be undone.</Trans>
        </p>
      </ConfirmDialog>
    </Panel>
  );
}
