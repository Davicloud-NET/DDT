// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { DrawerFooter } from "@/rules/DrawerFooter";
import { DrawerNotices } from "@/rules/DrawerNotices";
import { DrawerTitle } from "@/rules/DrawerTitle";
import { Drawer } from "@/ui/Drawer";
import { TextField } from "@/ui/TextField";

import type { ApplicationBundleView, ApplicationView } from "../applications";
import { errorsAt } from "../drawer/errorsAt";
import { OrderedPicker } from "../drawer/OrderedPicker";
import { UsedBySection } from "../drawer/UsedBySection";
import { useBundleForm } from "./useBundleForm";

interface BundleDrawerProps {
  // Null for a new bundle.
  bundle: ApplicationBundleView | null;
  library: readonly ApplicationView[];
  onClose: () => void;
  onDelete: (bundle: ApplicationBundleView) => void;
}

export function BundleDrawer({ bundle, library, onClose, onDelete }: BundleDrawerProps) {
  const { t } = useLingui();
  const form = useBundleForm(bundle, onClose);
  const { base, edit, busy, theirs, findings, change } = form;
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
        <DrawerTitle over={t`Bundle`}>
          {base === null ? <Trans>New bundle</Trans> : base.name}
        </DrawerTitle>
      }
      footer={
        <DrawerFooter
          saveLabel={<Trans>Save bundle</Trans>}
          deleteLabel={<Trans>Delete bundle</Trans>}
          isSaveDisabled={busy || edit.name.trim() === "" || form.gone}
          canDelete={base !== null && !form.gone}
          isBusy={busy}
          onSave={form.submit}
          onCancel={onClose}
          onDelete={() => {
            if (base !== null) {
              onClose();
              onDelete(base);
            }
          }}
        />
      }
    >
      <DrawerNotices
        savedMeanwhile={
          theirs === null
            ? null
            : who === null
              ? t`Someone else saved this bundle while you were editing it.`
              : t`${who} saved this bundle while you were editing it.`
        }
        gone={
          form.gone ? <Trans>Someone deleted this bundle while you were editing it.</Trans> : null
        }
        loose={form.loose}
        refused={form.refused}
        isBusy={busy}
        onTakeTheirs={form.takeTheirs}
        onKeepMine={form.keepMine}
      />

      <TextField
        label={<Trans>Name</Trans>}
        value={edit.name}
        onChange={(name) => {
          change({ name });
        }}
        {...errorsAt(findings, "name")}
      />
      <TextField
        label={<Trans>Description</Trans>}
        multiline
        rows={2}
        value={edit.description}
        onChange={(description) => {
          change({ description });
        }}
        {...errorsAt(findings, "description")}
      />
      <OrderedPicker
        label={<Trans>Applications</Trans>}
        field="members"
        ids={edit.members}
        options={library}
        addLabel={t`Add an application`}
        findings={findings}
        onChange={(members) => {
          change({ members });
        }}
      />
      {base !== null ? <UsedBySection uses={base.usedBy} /> : null}
    </Drawer>
  );
}
