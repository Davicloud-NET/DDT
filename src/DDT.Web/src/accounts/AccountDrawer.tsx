// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus, IconX } from "@tabler/icons-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useId, useState, type ReactNode } from "react";
import { Button as AriaButton } from "react-aria-components";

import { formattingLocale } from "@/i18n/i18n";
import { ApiError } from "@/lib/api";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { DrawerTitle, SavedMeanwhile } from "@/rules/drawerParts";
import { conflictOf, noFindings, refusalFindings, unplaced } from "@/rules/refusals";
import { fieldFindings, type Findings } from "@/sequences/problems";
import type { SecretAction, SecretState } from "@/settings/settings";
import { ReauthDialog } from "@/settings/SettingsParts";
import { Button } from "@/ui/Button";
import { Checkbox } from "@/ui/Checkbox";
import { ConfirmDialog } from "@/ui/Dialog";
import { Drawer } from "@/ui/Drawer";
import { Notice } from "@/ui/Notice";
import { StateTag } from "@/ui/StateTag";
import { TextField } from "@/ui/TextField";

import {
  accountEditOf,
  accountRequestOf,
  createAccount,
  deleteAccount,
  putAccount,
  reachesNewDestination,
  removeAccounts,
  saveAccount,
  type AccountEdit,
  type AccountView,
} from "./accounts";

function isShown(field: string): boolean {
  return /^(name|userName|domain|hosts|runAs|password)(\.|\[|$)/.test(field);
}

// Whether the server refused because it wants the password of the person again.
function wantsReauthentication(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    error.status === 403 &&
    error.problem?.code !== "stepAccount.apiToken"
  );
}

// Changing an account needs the password of the person who changes it, as the server asks.
function ReauthForAccounts({
  isOpen,
  onAccepted,
  onCancel,
  confirmLabel,
}: {
  isOpen: boolean;
  onAccepted: () => void;
  onCancel: () => void;
  confirmLabel?: ReactNode;
}) {
  return (
    <ReauthDialog
      isOpen={isOpen}
      onAccepted={onAccepted}
      onCancel={onCancel}
      {...(confirmLabel === undefined ? {} : { confirmLabel })}
      reason={
        <Trans>
          An account reaches machines with whatever it may do in the domain, so changing one needs
          your password again.
        </Trans>
      }
    />
  );
}

// An account's form: its name, the user name and where it may be used, and its password, which is only ever typed
// here: the server never sends it back, and a save sends it once. Changing the user name, the domain or adding a
// server needs the password again, since a stored one goes only where it was entered for.
export function AccountDrawer({
  account,
  onClose,
}: {
  // Null for a new account.
  account: AccountView | null;
  onClose: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const [base, setBase] = useState<AccountView | null>(account);
  const [edit, setEdit] = useState<AccountEdit>(() => accountEditOf(account));
  const [findings, setFindings] = useState<Findings>(noFindings);
  const [theirs, setTheirs] = useState<AccountView | null>(null);
  const [gone, setGone] = useState(false);
  const [deleting, setDeleting] = useState(false);
  // The revision a save goes out with again once the password was entered.
  const [reauth, setReauth] = useState<number | null>(null);

  const change = (patch: Partial<AccountEdit>) => {
    setEdit((current) => ({ ...current, ...patch }));
  };

  const save = useMutation({
    mutationFn: (revision: number) =>
      base === null
        ? createAccount(accountRequestOf(0, edit))
        : saveAccount(base.id, accountRequestOf(revision, edit)),
    onSuccess: (saved) => {
      putAccount(queryClient, saved);
      onClose();
    },
    onError: (error, revision) => {
      if (wantsReauthentication(error)) {
        setReauth(revision);
        return;
      }

      const current = conflictOf(error) as AccountView | null;

      if (current !== null) {
        putAccount(queryClient, current);
        setTheirs(current);
        return;
      }

      if (error instanceof ApiError && error.status === 404) {
        setGone(true);
        return;
      }

      const refused = refusalFindings(error) ?? noFindings;

      setFindings(refused);

      // The server wants the password typed again, so the field for it opens.
      if (
        fieldFindings(refused, "password").problems.length > 0 &&
        edit.password.action !== "Set"
      ) {
        change({ password: { action: "Set", value: "" } });
      }
    },
  });

  const name = base?.name ?? "";
  const busy = save.isPending;
  const loose = unplaced(findings, isShown);
  const refused =
    save.isError &&
    !wantsReauthentication(save.error) &&
    refusalFindings(save.error) === null &&
    conflictOf(save.error) === null &&
    !gone
      ? save.error.message
      : null;
  const who = theirs?.updatedBy ?? null;
  const moved = base !== null && base.password.isSet && reachesNewDestination(base, edit);
  const errors = (field: string) => fieldFindings(findings, field).problems;

  return (
    <Drawer
      isOpen
      onOpenChange={(open) => {
        if (!open && !busy) {
          onClose();
        }
      }}
      title={
        <DrawerTitle over={t`Account for steps`}>
          {base === null ? <Trans>New account</Trans> : name}
        </DrawerTitle>
      }
      footer={
        <>
          <Button
            variant="primary"
            isDisabled={busy || edit.name.trim() === "" || edit.userName.trim() === "" || gone}
            onPress={() => {
              save.mutate(base?.revision ?? 0);
            }}
          >
            <Trans>Save account</Trans>
          </Button>
          <Button variant="secondary" isDisabled={busy} onPress={onClose}>
            <Trans>Cancel</Trans>
          </Button>
          {base !== null && !gone ? (
            <Button
              variant="quiet"
              className="ml-auto text-fail-text hover:text-fail-text"
              isDisabled={busy}
              onPress={() => {
                setDeleting(true);
              }}
            >
              <Trans>Delete account</Trans>
            </Button>
          ) : null}
        </>
      }
    >
      {theirs !== null ? (
        <SavedMeanwhile
          title={
            who === null
              ? t`Someone else saved this account while you were editing it.`
              : t`${who} saved this account while you were editing it.`
          }
          isBusy={busy}
          onTakeTheirs={() => {
            setBase(theirs);
            setEdit(accountEditOf(theirs));
            setFindings(noFindings);
            setTheirs(null);
            save.reset();
          }}
          onKeepMine={() => {
            setBase(theirs);
            save.mutate(theirs.revision);
          }}
        />
      ) : null}

      {gone ? (
        <Notice tone="attention">
          <Trans>Someone deleted this account while you were editing it.</Trans>
        </Notice>
      ) : null}

      {loose.length > 0 ? <Notice tone="fail">{loose.join(" ")}</Notice> : null}
      {refused !== null ? <Notice tone="fail">{refused}</Notice> : null}

      <TextField
        label={<Trans>Name</Trans>}
        hint={<Trans>How steps and this page name it, such as Join account.</Trans>}
        value={edit.name}
        onChange={(text) => {
          change({ name: text });
        }}
        isInvalid={errors("name").length > 0}
        errorMessage={errors("name").join(" ")}
      />

      <TextField
        label={<Trans>User name</Trans>}
        hint={<Trans>With its domain, as DOMAIN\user or user@corp.example.</Trans>}
        mono
        autoComplete="off"
        spellCheck="false"
        value={edit.userName}
        onChange={(text) => {
          change({ userName: text });
        }}
        isInvalid={errors("userName").length > 0}
        errorMessage={errors("userName").join(" ")}
      />

      <TextField
        label={<Trans>Domain</Trans>}
        hint={
          <Trans>
            Optional. The domain a Join the domain step may join with it, such as corp.example.
          </Trans>
        }
        mono
        autoComplete="off"
        spellCheck="false"
        value={edit.domain}
        onChange={(text) => {
          change({ domain: text });
        }}
        isInvalid={errors("domain").length > 0}
        errorMessage={errors("domain").join(" ")}
      />

      <HostsEditor
        rows={edit.hosts}
        findings={findings}
        onChange={(hosts) => {
          change({ hosts });
        }}
      />

      <div data-field="runAs">
        <Checkbox
          isSelected={edit.runAs}
          onChange={(runAs) => {
            change({ runAs });
          }}
        >
          <span className="flex flex-col">
            <span>
              <Trans>Scripts may run as this account</Trans>
            </span>
            <span className="type-small text-muted">
              <Trans>
                In Windows, after the image is applied. A script in Windows PE runs as the system.
              </Trans>
            </span>
          </span>
        </Checkbox>
      </div>

      <PasswordSetting
        state={base?.password ?? null}
        action={edit.password}
        isNew={base === null}
        moved={moved}
        errors={errors("password")}
        onChange={(password) => {
          change({ password });
        }}
      />

      <ReauthForAccounts
        isOpen={reauth !== null}
        onAccepted={() => {
          const revision = reauth ?? 0;

          setReauth(null);
          save.mutate(revision);
        }}
        onCancel={() => {
          setReauth(null);
          save.reset();
        }}
      />

      {deleting && base !== null ? (
        <DeleteAccountDialog
          account={base}
          onClose={() => {
            setDeleting(false);
          }}
          onDeleted={onClose}
        />
      ) : null}
    </Drawer>
  );
}

// The servers the account may connect shares on, one per row, as share paths name them.
function HostsEditor({
  rows,
  findings,
  onChange,
}: {
  rows: AccountEdit["hosts"];
  findings: Findings;
  onChange: (rows: AccountEdit["hosts"]) => void;
}) {
  const { t } = useLingui();
  const labelId = useId();
  const own = fieldFindings(findings, "hosts").problems;

  return (
    <div role="group" aria-labelledby={labelId} data-field="hosts" className="flex flex-col gap-2">
      <span id={labelId} className="type-label text-ink">
        <Trans>Servers</Trans>
      </span>
      <span className="-mt-1 type-small text-muted">
        <Trans>
          The servers a step may connect shares on with this account, such as files.corp.example.
          None, and it connects no share.
        </Trans>
      </span>
      {rows.map((row, index) => {
        const number = index + 1;
        const field = `hosts[${String(index)}]`;
        const problems = fieldFindings(findings, field).problems;

        return (
          <div
            key={row.key}
            data-field={field}
            className="grid grid-cols-[minmax(0,1fr)_2rem] items-start gap-1.5"
          >
            <TextField
              label={<span className="sr-only">{t`Server ${number}`}</span>}
              mono
              autoComplete="off"
              spellCheck="false"
              value={row.host}
              onChange={(host) => {
                onChange(rows.map((other) => (other.key === row.key ? { ...other, host } : other)));
              }}
              isInvalid={problems.length > 0}
              errorMessage={problems.join(" ")}
            />
            <AriaButton
              aria-label={t`Remove server ${number}`}
              className="mt-0.75 flex size-8 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus"
              onPress={() => {
                onChange(rows.filter((other) => other.key !== row.key));
              }}
            >
              <IconX size={14} stroke={2} />
            </AriaButton>
          </div>
        );
      })}
      {own.map((message) => (
        <p key={message} className="type-small text-fail-text">
          {message}
        </p>
      ))}
      <AriaButton
        className="flex cursor-pointer items-center gap-1.5 self-start rounded-key px-1 py-1 type-label text-ink key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus"
        onPress={() => {
          onChange([...rows, { key: crypto.randomUUID(), host: "" }]);
        }}
      >
        <IconPlus aria-hidden="true" size={14} stroke={2} />
        <Trans>Add a server</Trans>
      </AriaButton>
    </div>
  );
}

// The password, which the page never sees: it says whether one is set, and a new one is typed only to be sent. It is
// kept as it is, replaced or cleared on save.
function PasswordSetting({
  state,
  action,
  isNew,
  moved,
  errors,
  onChange,
}: {
  state: SecretState | null;
  action: SecretAction;
  isNew: boolean;
  // The user name, the domain or a server changed, so a stored password has to be entered again.
  moved: boolean;
  errors: string[];
  onChange: (action: SecretAction) => void;
}) {
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
        <span className="flex flex-wrap items-center gap-2">
          {action.action === "Clear" ? (
            <StateTag tone="attention">{t`Cleared on save`}</StateTag>
          ) : state?.unreadable === true ? (
            <span className="type-small text-fail-text">{t`Cannot be read, enter it again`}</span>
          ) : stored ? (
            <StateTag tone="ok">{t`Set`}</StateTag>
          ) : (
            <StateTag tone="idle">{t`Not set`}</StateTag>
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
      {isNew ? null : (
        <span className="flex gap-2">
          {action.action === "Keep" ? (
            <>
              <Button
                size="sm"
                onPress={() => {
                  onChange({ action: "Set", value: "" });
                }}
              >
                {stored ? <Trans>Replace</Trans> : <Trans>Set</Trans>}
              </Button>
              {stored ? (
                <Button
                  size="sm"
                  variant="quiet"
                  onPress={() => {
                    onChange({ action: "Clear" });
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
                onChange({ action: "Keep" });
              }}
            >
              {stored ? <Trans>Keep the stored one</Trans> : <Trans>Leave it unset</Trans>}
            </Button>
          )}
        </span>
      )}
    </div>
  );
}

// Asks before deleting an account. While a sequence names it the server keeps it, so the dialog says which to change
// instead of offering the deletion. Like every change of an account, it needs the password of the person again.
export function DeleteAccountDialog({
  account,
  onClose,
  onDeleted,
}: {
  account: AccountView;
  onClose: () => void;
  onDeleted: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const id = account.id;
  const name = account.name;
  const [reauth, setReauth] = useState(false);

  const remove = useMutation({
    mutationFn: () => deleteAccount(id),
    onSuccess: () => {
      removeAccounts(queryClient, [id]);
      onDeleted();
    },
    onError: (error) => {
      if (wantsReauthentication(error)) {
        setReauth(true);
      } else if (error instanceof ApiError && error.status === 404) {
        // Someone else deleted it first, which is what was asked for.
        removeAccounts(queryClient, [id]);
        onDeleted();
      }
    },
  });

  const sequences = account.usedBy.map((use) => use.sequenceName);
  const count = sequences.length;
  const list = new Intl.ListFormat(formattingLocale(), { type: "conjunction" }).format(sequences);
  const blocker =
    count === 0
      ? null
      : plural(count, {
          one: `The sequence ${list} uses this account. Choose another account there first.`,
          other: `The sequences ${list} use this account. Choose another account there first.`,
        });

  return (
    <>
      <ConfirmDialog
        isOpen
        onOpenChange={(open) => {
          if (!open) {
            onClose();
          }
        }}
        title={t`Delete ${name}?`}
        confirmLabel={<Trans>Delete account</Trans>}
        danger
        isBusy={remove.isPending}
        isConfirmDisabled={blocker !== null}
        error={
          remove.isError && !wantsReauthentication(remove.error) ? remove.error.message : undefined
        }
        onConfirm={() => {
          remove.mutate();
        }}
      >
        <p>
          {blocker ?? (
            <Trans>
              The account and its password are deleted. No sequence names it, so no run loses it.
            </Trans>
          )}
        </p>
      </ConfirmDialog>
      <ReauthForAccounts
        isOpen={reauth}
        confirmLabel={<Trans>Confirm and delete</Trans>}
        onAccepted={() => {
          setReauth(false);
          remove.mutate();
        }}
        onCancel={() => {
          setReauth(false);
          remove.reset();
        }}
      />
    </>
  );
}
