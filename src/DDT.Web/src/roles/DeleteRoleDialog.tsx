// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { ApiError } from "@/lib/api";
import { rulesQuery, type RuleView } from "@/rules/rules";
import { ruleNames } from "@/rules/ruleText";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

import { deleteRole, removeRole, rulesGiving, type MachineRoleView } from "./roles";
import { RuleLinks } from "./RuleLinks";

interface DeleteRoleDialogProps {
  role: MachineRoleView;
  rules: readonly RuleView[];
  onClose: () => void;
  onDeleted: () => void;
}

// Asks before deleting a machine role. While rules give it the server keeps it, so the dialog says which rules to
// change instead of offering the deletion; one that became so meanwhile is refused with 409, said the same way.
export function DeleteRoleDialog({ role, rules, onClose, onDeleted }: DeleteRoleDialogProps) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const id = role.id;
  const name = role.name;

  const remove = useMutation({
    mutationFn: () => deleteRole(id),
    onSuccess: () => {
      removeRole(queryClient, id);
      onDeleted();
    },
    onError: (error) => {
      // Someone else deleted it first, which is what was asked for.
      if (error instanceof ApiError && error.status === 404) {
        removeRole(queryClient, id);
        onDeleted();
      }

      // A rule gives it that this page does not know of yet, so the rules are read to name it.
      if (error instanceof ApiError && error.status === 409) {
        void queryClient.invalidateQueries({ queryKey: rulesQuery.queryKey });
      }
    },
  });

  const giving = rulesGiving(rules, id);
  const count = giving.length;
  const list = ruleNames(giving);
  const blocker =
    count === 0
      ? null
      : plural(count, {
          one: `${list} gives this machine role. Take it out of that rule, then delete the role.`,
          other: `${list} give this machine role. Take it out of those rules, then delete the role.`,
        });
  const refused = remove.error instanceof ApiError && remove.error.status === 409;

  return (
    <ConfirmDialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={t`Delete ${name}?`}
      confirmLabel={<Trans>Delete machine role</Trans>}
      danger
      isBusy={remove.isPending}
      isConfirmDisabled={blocker !== null}
      error={remove.isError && !(refused && blocker !== null) ? remove.error.message : undefined}
      onConfirm={() => {
        remove.mutate();
      }}
    >
      {blocker !== null ? (
        <>
          <p>{blocker}</p>
          <RuleLinks rules={giving} />
        </>
      ) : (
        <p>
          <Trans>No rule gives {name}, so deleting it changes the values of no machine.</Trans>
        </p>
      )}
    </ConfirmDialog>
  );
}
