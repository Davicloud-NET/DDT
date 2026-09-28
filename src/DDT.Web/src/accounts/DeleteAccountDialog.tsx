// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { formattingLocale } from "@/i18n/i18n";
import { ApiError } from "@/lib/api";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

import { deleteAccount, removeAccounts, wantsReauthentication, type AccountView } from "./accounts";
import { ReauthForAccounts } from "./ReauthForAccounts";

interface DeleteAccountDialogProps {
  account: AccountView;
  onClose: () => void;
  onDeleted: () => void;
}

// Asks before deleting an account. While a sequence names it the server keeps it, so the dialog says which to change
// instead of offering the deletion. Like every change of an account, it needs the password of the person again.
export function DeleteAccountDialog({ account, onClose, onDeleted }: DeleteAccountDialogProps) {
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
