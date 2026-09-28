// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { ConfirmDialog } from "@/ui/ConfirmDialog";

import type { Deletion } from "./useDeletion";

// Asks before an image or a package is deleted, and keeps the server's reason when it refuses.
export function DeleteDialog<T extends { id: string; name: string }>({
  deletion,
  confirmLabel,
  consequence,
}: {
  deletion: Deletion<T>;
  confirmLabel: ReactNode;
  consequence: (item: T) => string;
}) {
  const { item: deleting, mutation: remove } = deletion;

  return (
    <ConfirmDialog
      isOpen={deleting !== null}
      onOpenChange={(open) => {
        if (!open) {
          deletion.close();
        }
      }}
      title={deleting === null ? "" : <DeleteTitle name={deleting.name} />}
      confirmLabel={confirmLabel}
      danger
      isBusy={remove.isPending}
      error={remove.isError ? remove.error.message : undefined}
      onConfirm={() => {
        if (deleting !== null) {
          remove.mutate(deleting.id);
        }
      }}
    >
      <p>{deleting === null ? null : consequence(deleting)}</p>
    </ConfirmDialog>
  );
}

function DeleteTitle({ name }: { name: string }) {
  return <Trans>Delete {name}?</Trans>;
}
