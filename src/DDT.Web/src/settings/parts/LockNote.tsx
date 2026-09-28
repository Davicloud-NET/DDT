// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { SettingsLock } from "../settings";

// Says that configuration sets the field, so the page cannot, and how to take it out of configuration.
export function LockNote({ lock }: { lock: SettingsLock }) {
  const key = lock.configurationKey;
  const variable = lock.environmentVariable;

  return (
    <span className="type-small text-attention-text">
      {lock.storedDiffers ? (
        <Trans>
          Set in configuration as {key} ({variable}), which wins over the value stored here. Remove
          it there to change it on this page.
        </Trans>
      ) : (
        <Trans>
          Set in configuration as {key} ({variable}). Remove it there to change it on this page.
        </Trans>
      )}
    </span>
  );
}
