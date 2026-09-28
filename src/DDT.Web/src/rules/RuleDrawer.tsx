// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useState } from "react";

import type { Subject } from "@/conditions/conditionSubjects";
import type { MachineRoleView } from "@/roles/roles";
import { BuilderContext } from "@/sequences/builder/builderData";
import { useBuilderData } from "@/sequences/builder/useBuilderData";
import { EditorLock } from "@/sequences/editorLock";
import type { SequenceSummary } from "@/sequences/sequences";
import { Button } from "@/ui/Button";
import { Drawer } from "@/ui/Drawer";

import { DeleteRuleDialog } from "./DeleteRuleDialog";
import { RuleFields } from "./drawer/RuleFields";
import { RuleNotices } from "./drawer/RuleNotices";
import { useRuleForm } from "./drawer/useRuleForm";
import { DrawerFooter } from "./DrawerFooter";
import { DrawerTitle } from "./DrawerTitle";
import { noDeclarations } from "./ruleData";
import type { RuleView } from "./rules";

interface RuleDrawerProps {
  // Null for a new rule, which goes to the bottom.
  rule: RuleView | null;
  // How many rules there are, to number a new one.
  count: number;
  roles: readonly MachineRoleView[];
  sequences: readonly SequenceSummary[];
  subjects: readonly Subject[];
  canEdit: boolean;
  // A new rule saved with problems stays open as the rule it now is.
  onSaved: (rule: RuleView) => void;
  onClose: () => void;
}

// A rule's form, beside the list. Someone who may only look reads the same form with nothing to change.
export function RuleDrawer({
  rule,
  count,
  roles,
  sequences,
  subjects,
  canEdit,
  onSaved,
  onClose,
}: RuleDrawerProps) {
  const { t } = useLingui();
  const builder = useBuilderData(noDeclarations);
  const form = useRuleForm({ rule, onSaved, onClose });
  const [deleting, setDeleting] = useState(false);
  const { base, busy } = form;
  const number = (base?.position ?? count) + 1;
  const name = base?.name ?? "";

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
        <DrawerTitle over={t`Rule ${number}`}>
          {base === null ? <Trans>New rule</Trans> : name}
        </DrawerTitle>
      }
      footer={
        canEdit ? (
          <DrawerFooter
            saveLabel={<Trans>Save rule</Trans>}
            deleteLabel={<Trans>Delete rule</Trans>}
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
          <RuleNotices form={form} />
          <RuleFields
            form={form}
            roles={roles}
            sequences={sequences}
            subjects={subjects}
            canEdit={canEdit}
          />
        </EditorLock>
      </BuilderContext>

      {deleting && base !== null ? (
        <DeleteRuleDialog
          rule={base}
          onClose={() => {
            setDeleting(false);
          }}
          onDeleted={onClose}
        />
      ) : null}
    </Drawer>
  );
}
