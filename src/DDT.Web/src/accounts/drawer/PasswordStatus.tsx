// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import type { SecretAction } from "@/settings/settings";
import { FieldErrorText } from "@/ui/FieldErrorText";
import { StateTag } from "@/ui/StateTag";

interface PasswordStatusProps {
  action: Exclude<SecretAction["action"], "Set">;
  unreadable: boolean;
  stored: boolean;
  // When the stored password last changed, as a relative time.
  changed: string | null;
  errors: string[];
}

// The state of an account's password while no new one is typed: stored, not set, unreadable or cleared on save.
export function PasswordStatus({
  action,
  unreadable,
  stored,
  changed,
  errors,
}: PasswordStatusProps) {
  const { t } = useLingui();

  return (
    <span className="flex flex-wrap items-center gap-2">
      {action === "Clear" ? (
        <StateTag tone="attention">{t`Cleared on save`}</StateTag>
      ) : unreadable ? (
        <span className="type-small text-fail-text">{t`Cannot be read, enter it again`}</span>
      ) : stored ? (
        <StateTag tone="ok">{t`Set`}</StateTag>
      ) : (
        <StateTag tone="idle">{t`Not set`}</StateTag>
      )}
      {changed !== null && action === "Keep" ? (
        <span className="type-small text-muted">
          <Trans>changed {changed}</Trans>
        </span>
      ) : null}
      <FieldErrorText errors={errors} />
    </span>
  );
}
