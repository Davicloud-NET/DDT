// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";

import { ApiError } from "@/lib/api";
import type { RuleView } from "@/rules/rules";
import { ConfirmDialog } from "@/ui/Dialog";

import { deletionBlocker, deletionConsequence, removeSummary } from "./sequenceList";
import { deleteSequence, sequenceQuery, type SequenceSummary } from "./sequences";

// Asks before deleting a sequence and says what that does to the machines that use it. While a rule chooses the
// sequence the server keeps it, so the dialog says which rules to change instead of offering the deletion. The list
// drops the sequence when the server answers; the hub tells everyone else.
export function DeleteSequenceDialog({
  sequence,
  rules,
  activeRuns,
  onClose,
}: {
  sequence: SequenceSummary;
  // The rules that choose it.
  rules: readonly RuleView[];
  // The machines it is assigned to or running on now.
  activeRuns: number;
  onClose: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const id = sequence.id;
  const name = sequence.name;

  const gone = () => {
    removeSummary(queryClient, id);
    queryClient.removeQueries({ queryKey: sequenceQuery(id).queryKey });
    onClose();
  };

  const remove = useMutation({
    mutationFn: () => deleteSequence(id),
    onSuccess: gone,
    // Someone else deleted it first, which is what was asked for.
    onError: (error) => {
      if (error instanceof ApiError && error.status === 404) {
        gone();
      }
    },
  });

  const blocker = deletionBlocker(sequence, rules);

  return (
    <ConfirmDialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={t`Delete ${name}?`}
      confirmLabel={<Trans>Delete sequence</Trans>}
      danger
      isBusy={remove.isPending}
      isConfirmDisabled={blocker !== null}
      error={remove.isError ? remove.error.message : undefined}
      onConfirm={() => {
        remove.mutate();
      }}
    >
      {blocker !== null ? (
        <>
          <p>{blocker}</p>
          <p>
            <Link to="/deployment/rules" className="font-semibold text-ink underline">
              <Trans>Go to the rules</Trans>
            </Link>
          </p>
        </>
      ) : (
        deletionConsequence(sequence, activeRuns).map((sentence, index) => (
          <p key={index}>{sentence}</p>
        ))
      )}
    </ConfirmDialog>
  );
}
