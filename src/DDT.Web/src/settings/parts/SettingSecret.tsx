// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { FieldErrorText } from "@/ui/FieldErrorText";
import { StateTag } from "@/ui/StateTag";
import { TextField } from "@/ui/TextField";

import type { SecretAction, SecretState } from "../settings";

import { FieldFrame } from "./FieldFrame";
import type { SettingFieldProps } from "./fieldProps";

// A secret the server never sends back: it says only whether one is set. Typing a new one replaces it on save;
// clearing removes it.
export function SettingSecret<T>({ form, field, label, hint, canChange }: SettingFieldProps<T>) {
  const { t: translate } = useLingui();
  const state = form.view?.secrets[field] ?? null;
  const action = form.secrets[field] ?? { action: "Keep" };
  const errors = form.fieldErrors(field);
  const now = useNow(60_000);
  const changed =
    state?.updatedUtc === null || state?.updatedUtc === undefined
      ? null
      : relativeTime(state.updatedUtc, now);

  return (
    <FieldFrame form={form} field={field}>
      <div className="flex flex-col gap-1.5">
        <span className="type-label text-ink">{label}</span>
        {action.action === "Set" ? (
          <TextField
            label={translate`New value`}
            type="password"
            autoComplete="new-password"
            value={action.value}
            onChange={(value) => {
              form.setSecret(field, { action: "Set", value });
            }}
            isInvalid={errors.length > 0}
            errorMessage={errors.join(" ")}
          />
        ) : (
          <span className="flex flex-wrap items-center gap-2">
            <SecretTag state={state} action={action} />
            {changed !== null && action.action === "Keep" ? (
              <span className="type-small text-muted">
                <Trans>changed {changed}</Trans>
              </span>
            ) : null}
            <FieldErrorText errors={errors} />
          </span>
        )}
        {hint ? <span className="type-small text-muted">{hint}</span> : null}
        {canChange && form.lockOf(field) === null ? (
          <span className="flex gap-2">
            {action.action === "Keep" ? (
              <>
                <Button
                  size="sm"
                  onPress={() => {
                    form.setSecret(field, { action: "Set", value: "" });
                  }}
                >
                  {state?.isSet === true ? <Trans>Replace</Trans> : <Trans>Set</Trans>}
                </Button>
                {state?.isSet === true ? (
                  <Button
                    size="sm"
                    variant="quiet"
                    onPress={() => {
                      form.setSecret(field, { action: "Clear" });
                    }}
                  >
                    <Trans>Clear</Trans>
                  </Button>
                ) : null}
              </>
            ) : (
              <Button
                size="sm"
                variant="quiet"
                onPress={() => {
                  form.setSecret(field, { action: "Keep" });
                }}
              >
                <Trans>Keep the stored one</Trans>
              </Button>
            )}
          </span>
        ) : null}
      </div>
    </FieldFrame>
  );
}

function SecretTag({ state, action }: { state: SecretState | null; action: SecretAction }) {
  const { t: translate } = useLingui();

  return action.action === "Clear" ? (
    <StateTag tone="attention">{translate`Cleared on save`}</StateTag>
  ) : state?.unreadable === true ? (
    <StateTag tone="fail">{translate`Unreadable`}</StateTag>
  ) : state?.isSet === true ? (
    <StateTag tone="ok">{translate`Set`}</StateTag>
  ) : (
    <StateTag tone="idle">{translate`Not set`}</StateTag>
  );
}
