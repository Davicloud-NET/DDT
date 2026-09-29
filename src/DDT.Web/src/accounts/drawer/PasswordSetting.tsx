// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useId } from "react";

import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import type { SecretAction, SecretState } from "@/settings/settings";
import { TextField } from "@/ui/TextField";

import { PasswordActions } from "./PasswordActions";
import { PasswordStatus } from "./PasswordStatus";

interface PasswordSettingProps {
  state: SecretState | null;
  action: SecretAction;
  isNew: boolean;
  // The user name, the domain or a server changed, so a stored password has to be entered again.
  moved: boolean;
  errors: string[];
  onChange: (action: SecretAction) => void;
}

// The password field. The page never sees the password. It only shows whether one is set, and a new one is typed only
// to be sent. On save the password is kept, replaced or cleared.
export function PasswordSetting({
  state,
  action,
  isNew,
  moved,
  errors,
  onChange,
}: PasswordSettingProps) {
  const { t } = useLingui();
  const now = useNow(60_000);
  const labelId = useId();
  const changed =
    state?.updatedUtc === null || state?.updatedUtc === undefined
      ? null
      : relativeTime(state.updatedUtc, now);
  const stored = state?.isSet === true;

  return (
    <div
      role="group"
      aria-labelledby={labelId}
      data-field="password"
      className="flex flex-col gap-1.5"
    >
      <span id={labelId} className="type-label text-ink">
        <Trans>Password</Trans>
      </span>
      {action.action === "Set" ? (
        <TextField
          label={
            <span className="sr-only">{isNew ? t`Password of the account` : t`New password`}</span>
          }
          type="password"
          autoComplete="new-password"
          value={action.value}
          onChange={(value) => {
            onChange({ action: "Set", value });
          }}
          isInvalid={errors.length > 0}
          errorMessage={errors.join(" ")}
        />
      ) : (
        <PasswordStatus
          action={action.action}
          unreadable={state?.unreadable === true}
          stored={stored}
          changed={changed}
          errors={errors}
        />
      )}
      <span className="type-small text-muted">
        <Trans>
          Stored encrypted and never shown again. It goes only to the step that uses the account,
          while the step runs.
        </Trans>
      </span>
      {moved && action.action === "Keep" ? (
        <span className="type-small text-attention-text">
          <Trans>
            A new user name, domain or server needs the password again: the stored one goes only
            where it was entered for.
          </Trans>
        </span>
      ) : null}
      {isNew ? null : <PasswordActions action={action} stored={stored} onChange={onChange} />}
    </div>
  );
}
