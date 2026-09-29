// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { ApiError } from "@/lib/api";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

import { deleteRule, rulesQuery, sequenceResolutionsKey, type RuleView } from "./rules";

interface DeleteRuleDialogProps {
  rule: RuleView;
  onClose: () => void;
  onDeleted: () => void;
}

// Asks before deleting a rule. The server answers with the whole list, because the rules below move up one place.
export function DeleteRuleDialog({ rule, onClose, onDeleted }: DeleteRuleDialogProps) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const id = rule.id;
  const name = rule.name;

  const remove = useMutation({
    mutationFn: () => deleteRule(id),
    onSuccess: (list) => {
      queryClient.setQueryData(rulesQuery.queryKey, list);
      void queryClient.invalidateQueries({ queryKey: sequenceResolutionsKey });
      onDeleted();
    },
    // Someone else deleted it first, which is what was asked for.
    onError: (error) => {
      if (error instanceof ApiError && error.status === 404) {
        queryClient.setQueryData(rulesQuery.queryKey, (list) =>
          list?.filter((existing) => existing.id !== id),
        );
        onDeleted();
      }
    },
  });

  return (
    <ConfirmDialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={t`Delete ${name}?`}
      confirmLabel={<Trans>Delete rule</Trans>}
      danger
      isBusy={remove.isPending}
      error={remove.isError ? remove.error.message : undefined}
      onConfirm={() => {
        remove.mutate();
      }}
    >
      <p>
        <Trans>
          The machines it matches no longer get what it chooses, sets or gives; a rule below it may
          give them that instead.
        </Trans>
      </p>
      <p>
        <Trans>
          The rules below it move up a place. Runs that already started keep their values.
        </Trans>
      </p>
    </ConfirmDialog>
  );
}
