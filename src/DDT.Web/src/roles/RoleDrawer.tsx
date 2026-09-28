// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useState } from "react";

import { DrawerFooter } from "@/rules/DrawerFooter";
import { DrawerNotices } from "@/rules/DrawerNotices";
import { DrawerTitle } from "@/rules/DrawerTitle";
import { noDeclarations } from "@/rules/ruleData";
import type { RuleView } from "@/rules/rules";
import { BuilderContext } from "@/sequences/builder/builderData";
import { useBuilderData } from "@/sequences/builder/useBuilderData";
import { EditorLock } from "@/sequences/editorLock";
import { Button } from "@/ui/Button";
import { Drawer } from "@/ui/Drawer";

import { DeleteRoleDialog } from "./DeleteRoleDialog";
import { RoleFields } from "./drawer/RoleFields";
import { useRoleForm } from "./drawer/useRoleForm";
import type { MachineRoleView } from "./roles";

interface RoleDrawerProps {
  // Null for a new role.
  role: MachineRoleView | null;
  rules: readonly RuleView[];
  canEdit: boolean;
  onClose: () => void;
}

// A machine role's form. Someone who may only look reads the same form with nothing to change.
export function RoleDrawer({ role, rules, canEdit, onClose }: RoleDrawerProps) {
  const { t } = useLingui();
  const builder = useBuilderData(noDeclarations);
  const form = useRoleForm({ role, onClose });
  const [deleting, setDeleting] = useState(false);
  const { base, busy, theirs } = form;
  const name = base?.name ?? "";
  const who = theirs?.updatedBy ?? null;

  return (
    <Drawer
      isOpen
      wide
      onOpenChange={(open) => {
        if (!open && !busy) {
          onClose();
        }
      }}
      title={
        <DrawerTitle over={t`Machine role`}>
          {base === null ? <Trans>New machine role</Trans> : name}
        </DrawerTitle>
      }
      footer={
        canEdit ? (
          <DrawerFooter
            saveLabel={<Trans>Save machine role</Trans>}
            deleteLabel={<Trans>Delete machine role</Trans>}
            isSaveDisabled={busy || form.edit.name.trim() === "" || form.gone}
            canDelete={base !== null && !form.gone}
            isBusy={busy}
            onSave={form.submit}
            onCancel={onClose}
            onDelete={() => {
              setDeleting(true);
            }}
          />
        ) : (
          <Button variant="secondary" onPress={onClose}>
            <Trans>Close</Trans>
          </Button>
        )
      }
    >
      <BuilderContext value={builder}>
        <EditorLock value={!canEdit}>
          <DrawerNotices
            savedMeanwhile={
              theirs === null
                ? null
                : who === null
                  ? t`Someone else saved this machine role while you were editing it.`
                  : t`${who} saved this machine role while you were editing it.`
            }
            gone={
              form.gone ? (
                <Trans>Someone deleted this machine role while you were editing it.</Trans>
              ) : null
            }
            loose={form.loose}
            refused={form.refused}
            isBusy={busy}
            onTakeTheirs={form.takeTheirs}
            onKeepMine={form.keepMine}
          />
          <RoleFields form={form} rules={rules} />
        </EditorLock>
      </BuilderContext>

      {deleting && base !== null ? (
        <DeleteRoleDialog
          role={base}
          rules={rules}
          onClose={() => {
            setDeleting(false);
          }}
          onDeleted={onClose}
        />
      ) : null}
    </Drawer>
  );
}
