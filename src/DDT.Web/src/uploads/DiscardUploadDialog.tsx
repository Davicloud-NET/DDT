// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import type { ImageUploadSession } from "@/images/images";
import { formatBytes } from "@/lib/format";
import type { Deletion } from "@/library/useDeletion";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

// Asks before an unfinished upload is discarded, and keeps the server's reason when it refuses.
export function DiscardUploadDialog({ discard }: { discard: Deletion<ImageUploadSession> }) {
  const { item: discarding, mutation } = discard;

  return (
    <ConfirmDialog
      isOpen={discarding !== null}
      onOpenChange={(next) => {
        if (!next) {
          discard.close();
        }
      }}
      title={discarding === null ? "" : <DiscardTitle file={discarding.fileName} />}
      confirmLabel={<Trans>Discard upload</Trans>}
      danger
      isBusy={mutation.isPending}
      error={mutation.isError ? mutation.error.message : undefined}
      onConfirm={() => {
        if (discarding !== null) {
          mutation.mutate(discarding.id);
        }
      }}
    >
      <p>{discarding === null ? null : discardConsequence(discarding)}</p>
    </ConfirmDialog>
  );
}

function DiscardTitle({ file }: { file: string }) {
  return <Trans>Discard the upload of {file}?</Trans>;
}

function discardConsequence(session: ImageUploadSession): string {
  const sent = formatBytes(session.offset);
  const file = session.fileName;

  return t`The ${sent} of ${file} uploaded so far are deleted from the server. Choosing the file again starts the upload over.`;
}
