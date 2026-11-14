// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { fullTime, relativeTime } from "@/lib/relativeTime";
import { Button } from "@/ui/Button";
import { SecretValue } from "@/ui/SecretValue";
import { StateTag } from "@/ui/StateTag";

import type { RecoveryKeyView } from "./machineRecords";
import type { RecoveryKeyActions } from "./useRecoveryKeyActions";

interface RecoveryKeyRowProps {
  recoveryKey: RecoveryKeyView;
  // Null for anyone but an administrator, who sees no buttons.
  actions: RecoveryKeyActions | null;
  now: number;
}

// One key: its ID as the recovery screen shows it, its volume and date, and the password once revealed.
export function RecoveryKeyRow({ recoveryKey, actions, now }: RecoveryKeyRowProps) {
  const password = actions?.revealed[recoveryKey.id];
  const keyId = recoveryKey.protectorId.toUpperCase();

  return (
    <li className="flex flex-col gap-2 border-b border-line-soft py-2.5 last:border-b-0">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
        <span className="type-data break-all text-ink">{keyId}</span>
        <span className="type-small text-ink-2">{recoveryKey.volume}</span>
        <span className="type-small text-muted" title={fullTime(recoveryKey.createdUtc)}>
          {relativeTime(recoveryKey.createdUtc, now)}
        </span>
        {recoveryKey.replacedUtc !== null ? (
          <StateTag tone="retired">
            <Trans>Replaced</Trans>
          </StateTag>
        ) : null}
        {actions !== null ? (
          <span className="ml-auto flex gap-1.5">
            {password === undefined ? (
              <Button
                size="sm"
                isDisabled={actions.reveal.isPending}
                onPress={() => {
                  actions.reveal.mutate(recoveryKey.id);
                }}
              >
                <Trans>Reveal</Trans>
              </Button>
            ) : null}
            {recoveryKey.replacedUtc !== null ? (
              <Button
                size="sm"
                variant="quiet"
                className="text-fail-text"
                onPress={() => {
                  actions.remove.reset();
                  actions.setDeleteOn(recoveryKey.id);
                }}
              >
                <Trans>Delete</Trans>
              </Button>
            ) : null}
          </span>
        ) : null}
      </div>
      {password !== undefined ? (
        <SecretValue label={<Trans>Recovery password for {keyId}</Trans>} value={password} />
      ) : null}
    </li>
  );
}
