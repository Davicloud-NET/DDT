// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useState } from "react";

import { DrawerFooter } from "@/rules/DrawerFooter";
import { DrawerNotices } from "@/rules/DrawerNotices";
import { DrawerTitle } from "@/rules/DrawerTitle";
import { fieldFindings } from "@/sequences/problems";
import { Drawer } from "@/ui/Drawer";

import type { AccountView } from "./accounts";
import { DeleteAccountDialog } from "./DeleteAccountDialog";
import { AccountFields } from "./drawer/AccountFields";
import { PasswordSetting } from "./drawer/PasswordSetting";
import { useAccountForm } from "./drawer/useAccountForm";
import { ReauthForAccounts } from "./ReauthForAccounts";

interface AccountDrawerProps {
  // Null for a new account.
  account: AccountView | null;
  onClose: () => void;
}

// An account's form. The password is only ever typed here. The server never sends it back, and a save sends it once.
export function AccountDrawer({ account, onClose }: AccountDrawerProps) {
  const { t } = useLingui();
  const form = useAccountForm({ account, onClose });
  const [deleting, setDeleting] = useState(false);
  const { base, edit, busy, theirs } = form;
  const name = base?.name ?? "";
  const who = theirs?.updatedBy ?? null;

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
        <DrawerFooter
          saveLabel={<Trans>Save account</Trans>}
          deleteLabel={<Trans>Delete account</Trans>}
          isSaveDisabled={
            busy || edit.name.trim() === "" || edit.userName.trim() === "" || form.gone
          }
          canDelete={base !== null && !form.gone}
          isBusy={busy}
          onSave={form.submit}
          onCancel={onClose}
          onDelete={() => {
            setDeleting(true);
          }}
        />
      }
    >
      <DrawerNotices
        savedMeanwhile={
          theirs === null
            ? null
            : who === null
              ? t`Someone else saved this account while you were editing it.`
              : t`${who} saved this account while you were editing it.`
        }
        gone={
          form.gone ? <Trans>Someone deleted this account while you were editing it.</Trans> : null
        }
        loose={form.loose}
        refused={form.refused}
        isBusy={busy}
        onTakeTheirs={form.takeTheirs}
        onKeepMine={form.keepMine}
      />

      <AccountFields form={form} />

      <PasswordSetting
        state={base?.password ?? null}
        action={edit.password}
        isNew={base === null}
        moved={form.moved}
        errors={fieldFindings(form.findings, "password").problems}
        onChange={(password) => {
          form.change({ password });
        }}
      />

      <ReauthForAccounts
        isOpen={form.needsReauth}
        onAccepted={form.resendAfterReauth}
        onCancel={form.cancelReauth}
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
