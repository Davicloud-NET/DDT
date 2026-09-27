// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useId, useState, type ReactNode } from "react";

import { equalJson } from "@/lib/equalJson";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { Switch } from "@/ui/Checkbox";
import { NumberField } from "@/ui/Controls";
import { Dialog } from "@/ui/Dialog";
import { Panel } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { ListBoxItem, Select } from "@/ui/Select";
import { StateTag } from "@/ui/StateTag";
import { TextField } from "@/ui/TextField";

import { reauthenticate, type SettingsApplyState, type SettingsLock } from "./settings";
import { valueAt, type SettingsForm } from "./useSettingsForm";

// A section of settings as a panel: its fields, and a bar that saves or discards them, says who saved last, and shows
// how far each host has applied a section that restarts a subsystem.
export function SettingsSection<T>({
  form,
  title,
  description,
  children,
  canChange,
}: {
  form: SettingsForm<T>;
  title: ReactNode;
  description?: ReactNode;
  children: ReactNode;
  canChange: boolean;
}) {
  const now = useNow(30_000);
  const view = form.view;
  const savedBy = view?.updatedBy ?? null;
  const savedWhen =
    view?.updatedUtc === null || view?.updatedUtc === undefined
      ? null
      : relativeTime(view.updatedUtc, now);

  return (
    <Panel title={title}>
      {description ? <p className="max-w-[80ch] text-ink-2">{description}</p> : null}

      {form.query.isError ? (
        <Notice tone="fail">
          <Trans>These settings could not be loaded.</Trans>
        </Notice>
      ) : null}

      {form.changedElsewhere ? (
        <Notice tone="attention">
          <span className="flex flex-wrap items-center gap-3">
            <Trans>Someone else saved these settings while you were changing them.</Trans>
            <Button size="sm" onPress={form.discard}>
              <Trans>Show theirs</Trans>
            </Button>
          </span>
        </Notice>
      ) : null}

      <div className="flex flex-col gap-4">{children}</div>

      {form.refusal?.kind === "stale" ? (
        <Notice tone="attention">
          <span className="flex flex-wrap items-center gap-3">
            {form.refusal.message}
            <Button size="sm" onPress={form.discard}>
              <Trans>Show theirs</Trans>
            </Button>
          </span>
        </Notice>
      ) : null}
      {form.refusal?.kind === "other" ? <Notice tone="fail">{form.refusal.message}</Notice> : null}
      <SectionErrors form={form} />

      {view?.apply !== null && view?.apply !== undefined && view.apply.length > 0 ? (
        <ApplyStates states={view.apply} version={view.version} />
      ) : null}

      <div className="-mx-4 -mb-4 flex flex-wrap items-center gap-3 border-t border-line-soft px-4 py-3">
        <span className="flex-1 type-small text-muted">
          {savedWhen === null ? (
            <Trans>Never changed on this page.</Trans>
          ) : savedBy === null ? (
            <Trans>Saved {savedWhen}.</Trans>
          ) : (
            <Trans>
              Saved {savedWhen} by {savedBy}.
            </Trans>
          )}
        </span>
        {canChange ? (
          <>
            <Button isDisabled={!form.dirty || form.saving} onPress={form.discard}>
              <Trans>Discard</Trans>
            </Button>
            <Button
              variant="primary"
              isDisabled={!form.dirty || form.saving}
              onPress={() => {
                form.save();
              }}
            >
              <Trans>Save</Trans>
            </Button>
          </>
        ) : null}
      </div>

      <WarningsDialog form={form} />
      <ReauthDialog
        isOpen={form.needsReauth}
        onAccepted={form.retryAfterReauth}
        onCancel={form.cancelReauth}
      />
    </Panel>
  );
}

// What a refused save or a stored problem says about the section as a whole rather than one field, such as a missing
// directory proof, under the fields and next to the save that was refused.
function SectionErrors<T>({ form }: { form: SettingsForm<T> }) {
  const errors = form.fieldErrors("");
  const refused = form.refusal?.kind === "invalid" ? form.refusal : null;
  const fieldsRefused = refused !== null && Object.keys(refused.fields).some((key) => key !== "");

  if (refused === null && errors.length === 0) {
    return null;
  }

  return (
    <Notice tone="fail">
      <span className="flex flex-col gap-1.5">
        {refused === null ? null : fieldsRefused ? (
          <span>
            <Trans>Nothing was saved. The fields marked above say why.</Trans>
          </span>
        ) : (
          <span>
            <Trans>Nothing was saved.</Trans>
          </span>
        )}
        {errors.length === 1 ? <span>{errors[0]}</span> : null}
        {errors.length > 1 ? (
          <ul className="flex list-disc flex-col gap-1 pl-5">
            {errors.map((error, index) => (
              <li key={index}>{error}</li>
            ))}
          </ul>
        ) : null}
      </span>
    </Notice>
  );
}

// How far each host has applied a section that rebuilds a subsystem, as of the section's version.
export function ApplyStates({
  states,
  version,
}: {
  states: SettingsApplyState[];
  version: number;
}) {
  const { t: translate } = useLingui();

  return (
    <ul aria-label={translate`Where it applies`} className="flex flex-col gap-1.5">
      {states.map((state) => {
        const host = state.host;
        const pending = state.state === "Pending" || state.version < version;

        return (
          <li key={host} className="flex flex-wrap items-center gap-2 type-small">
            <StateTag tone={state.state === "Failed" ? "fail" : pending ? "idle" : "ok"}>
              {state.state === "Failed"
                ? translate`Failed`
                : pending
                  ? translate`Pending`
                  : translate`Applied`}
            </StateTag>
            <span className="type-data text-ink">{host}</span>
            {/* A failure's reason can be long, so it reads as a line of its own. */}
            {state.message !== null ? (
              <span
                className={`min-w-0 break-words text-ink-2 ${state.state === "Failed" ? "basis-full" : ""}`}
              >
                {state.message}
              </span>
            ) : null}
          </li>
        );
      })}
    </ul>
  );
}

// A group of fields inside a section, under a heading of its own.
export function SettingsGroup({ title, children }: { title: ReactNode; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3 border-t border-line-soft pt-4 first:border-t-0 first:pt-0">
      <h3 className="type-label text-ink">{title}</h3>
      {children}
    </section>
  );
}

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

interface FieldProps<T> {
  form: SettingsForm<T>;
  field: string;
  label: ReactNode;
  hint?: ReactNode;
  canChange: boolean;
}

function FieldFrame<T>({
  form,
  field,
  children,
}: {
  form: SettingsForm<T>;
  field: string;
  children: ReactNode;
}) {
  const lock = form.lockOf(field);

  return (
    <div className="flex flex-col gap-1">
      {children}
      {lock === null ? null : <LockNote lock={lock} />}
    </div>
  );
}

export function SettingText<T>({
  form,
  field,
  label,
  hint,
  canChange,
  mono = false,
  placeholder,
}: FieldProps<T> & { mono?: boolean; placeholder?: string }) {
  const value = valueAt(form.values, field);
  const errors = form.fieldErrors(field);

  return (
    <FieldFrame form={form} field={field}>
      <TextField
        label={label}
        {...(hint === undefined ? {} : { hint })}
        {...(placeholder === undefined ? {} : { placeholder })}
        mono={mono}
        value={typeof value === "string" ? value : ""}
        onChange={(next) => {
          form.change(field, next === "" ? null : next);
        }}
        isReadOnly={!canChange || form.lockOf(field) !== null}
        isInvalid={errors.length > 0}
        errorMessage={errors.join(" ")}
      />
    </FieldFrame>
  );
}

export function SettingNumber<T>({
  form,
  field,
  label,
  hint,
  canChange,
  minValue,
  maxValue,
}: FieldProps<T> & { minValue?: number; maxValue?: number }) {
  const value = valueAt(form.values, field);
  const errors = form.fieldErrors(field);

  return (
    <FieldFrame form={form} field={field}>
      <NumberField
        label={label}
        {...(hint === undefined ? {} : { hint })}
        {...(minValue === undefined ? {} : { minValue })}
        {...(maxValue === undefined ? {} : { maxValue })}
        value={typeof value === "number" ? value : Number.NaN}
        onChange={(next) => {
          form.change(field, Number.isNaN(next) ? null : next);
        }}
        isReadOnly={!canChange || form.lockOf(field) !== null}
        isInvalid={errors.length > 0}
        errorMessage={errors.join(" ")}
        className="max-w-60"
      />
    </FieldFrame>
  );
}

export function SettingSwitch<T>({ form, field, label, hint, canChange }: FieldProps<T>) {
  const value = valueAt(form.values, field);
  const errors = form.fieldErrors(field);

  return (
    <FieldFrame form={form} field={field}>
      <Switch
        isSelected={value === true}
        onChange={(next) => {
          form.change(field, next);
        }}
        isReadOnly={!canChange || form.lockOf(field) !== null}
      >
        {label}
      </Switch>
      {hint ? <span className="type-small text-muted">{hint}</span> : null}
      {errors.length > 0 ? (
        <span className="type-small text-fail-text">{errors.join(" ")}</span>
      ) : null}
    </FieldFrame>
  );
}

export interface SettingOption {
  // The value as the section stores it, such as "Ldaps".
  id: string;
  label: string;
  description?: ReactNode;
}

// One value of a closed list, such as a transport or a role. With empty set, the field can also hold null, shown as
// that option's label.
export function SettingSelect<T>({
  form,
  field,
  label,
  hint,
  canChange,
  options,
  empty,
  className = "max-w-80",
}: FieldProps<T> & { options: SettingOption[]; empty?: string; className?: string }) {
  const value = valueAt(form.values, field);
  const errors = form.fieldErrors(field);
  const locked = !canChange || form.lockOf(field) !== null;
  const all = empty === undefined ? options : [{ id: "", label: empty }, ...options];

  return (
    <FieldFrame form={form} field={field}>
      <Select
        label={label}
        {...(hint === undefined ? {} : { hint })}
        value={typeof value === "string" ? value : ""}
        onChange={(key) => {
          if (key !== null) {
            form.change(field, key === "" ? null : String(key));
          }
        }}
        isDisabled={locked}
        isInvalid={errors.length > 0}
        errorMessage={errors.join(" ")}
        className={className}
      >
        {all.map((option) => (
          <ListBoxItem
            key={option.id}
            id={option.id}
            textValue={option.label}
            {...(option.description === undefined ? {} : { description: option.description })}
          >
            {option.label}
          </ListBoxItem>
        ))}
      </Select>
    </FieldFrame>
  );
}

// A list typed one entry per line, such as networks or addresses.
export function SettingLines<T>({ form, field, label, hint, canChange }: FieldProps<T>) {
  const value = valueAt(form.values, field);
  const lines = Array.isArray(value)
    ? value.filter((line): line is string => typeof line === "string")
    : [];
  const errors = form.fieldErrors(field);
  // What was typed, blank lines and spaces included, shown while it still says what the form holds; a discard or a
  // change saved elsewhere replaces it.
  const [text, setText] = useState<string | null>(null);
  const shown = text !== null && equalJson(linesOf(text), lines) ? text : lines.join("\n");

  return (
    <FieldFrame form={form} field={field}>
      <TextField
        label={label}
        {...(hint === undefined ? {} : { hint })}
        multiline
        rows={4}
        mono
        value={shown}
        onChange={(next) => {
          setText(next);
          form.change(field, linesOf(next));
        }}
        onBlur={() => {
          setText(null);
        }}
        isReadOnly={!canChange || form.lockOf(field) !== null}
        isInvalid={errors.length > 0}
        errorMessage={errors.join(" ")}
      />
    </FieldFrame>
  );
}

function linesOf(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== "");
}

// A secret the server never sends back: it says only whether one is set. Typing a new one replaces it on save;
// clearing removes it.
export function SettingSecret<T>({ form, field, label, hint, canChange }: FieldProps<T>) {
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
            {action.action === "Clear" ? (
              <StateTag tone="attention">{translate`Cleared on save`}</StateTag>
            ) : state?.unreadable === true ? (
              <StateTag tone="fail">{translate`Unreadable`}</StateTag>
            ) : state?.isSet === true ? (
              <StateTag tone="ok">{translate`Set`}</StateTag>
            ) : (
              <StateTag tone="idle">{translate`Not set`}</StateTag>
            )}
            {changed !== null && action.action === "Keep" ? (
              <span className="type-small text-muted">
                <Trans>changed {changed}</Trans>
              </span>
            ) : null}
            {errors.length > 0 ? (
              <span className="type-small text-fail-text">{errors.join(" ")}</span>
            ) : null}
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

// Warnings the server wants confirmed before it saves, such as a network wider than a /16.
function WarningsDialog<T>({ form }: { form: SettingsForm<T> }) {
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
          <li key={index}>{warning.message}</li>
        ))}
      </ul>
    </Dialog>
  );
}

// Fields that grant roles or trust, and actions such as the agent upload, need the password again, as the server
// asks. The token it gives lasts a few minutes, so several saves in a row ask once. onAccepted runs once the server
// took the password, to send what it refused again.
export function ReauthDialog({
  isOpen,
  onAccepted,
  onCancel,
  reason,
  confirmLabel,
}: {
  isOpen: boolean;
  onAccepted: () => void;
  onCancel: () => void;
  // Why the password is needed, where it is not a section's save.
  reason?: ReactNode;
  confirmLabel?: ReactNode;
}) {
  const formId = useId();
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const close = () => {
    setPassword("");
    setCode("");
    setError(null);
    onCancel();
  };

  return (
    <Dialog
      isOpen={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          close();
        }
      }}
      title={<Trans>Confirm it is you</Trans>}
      isBusy={busy}
      footer={
        <>
          <Button onPress={close} isDisabled={busy}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            isDisabled={busy || password === ""}
          >
            {confirmLabel ?? <Trans>Confirm and save</Trans>}
          </Button>
        </>
      }
    >
      <form
        id={formId}
        className="flex flex-col gap-3"
        onSubmit={(event) => {
          event.preventDefault();
          setBusy(true);
          setError(null);
          reauthenticate(password, code.trim() === "" ? null : code.trim())
            .then(() => {
              setPassword("");
              setCode("");
              onAccepted();
            })
            .catch((failure: unknown) => {
              setError(failure instanceof Error ? failure.message : t`That did not work.`);
            })
            .finally(() => {
              setBusy(false);
            });
        }}
      >
        <p>
          {reason ?? (
            <Trans>
              These settings decide who signs in and what machines trust, so they need your password
              again.
            </Trans>
          )}
        </p>
        <TextField
          label={<Trans>Password</Trans>}
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={setPassword}
          autoFocus
        />
        <TextField
          label={<Trans>Authenticator code, if you use one</Trans>}
          inputMode="numeric"
          autoComplete="one-time-code"
          mono
          value={code}
          onChange={setCode}
          className="max-w-60"
        />
        {error !== null ? <Notice tone="fail">{error}</Notice> : null}
      </form>
    </Dialog>
  );
}
