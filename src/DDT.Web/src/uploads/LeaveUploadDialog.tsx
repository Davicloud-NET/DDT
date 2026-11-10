// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { percentOf } from "@/lib/format";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

import type { LeaveGuard } from "./useLeaveGuard";
import type { UploadRun } from "./useResumableUpload";

// Asks whether to leave the page while a file uploads or the server checks it.
export function LeaveUploadDialog({
  leaving,
  run,
}: {
  leaving: LeaveGuard;
  run: UploadRun | null;
}) {
  return (
    <ConfirmDialog
      isOpen={leaving.status === "blocked" && run !== null}
      onOpenChange={(next) => {
        if (!next) {
          leaving.reset?.();
        }
      }}
      {...(run === null ? { title: "", confirmLabel: "" } : leaveQuestion(run))}
      danger
      onConfirm={() => {
        leaving.proceed?.();
      }}
    >
      <p>
        {run === null
          ? null
          : run.progress.phase === "verifying"
            ? t`The server goes on checking it. If it refuses the file, you won't see why.`
            : leaveWhileUploading(run)}
      </p>
    </ConfirmDialog>
  );
}

// What leaving the page means at this point of the upload.
function leaveQuestion(run: UploadRun): { title: ReactNode; confirmLabel: ReactNode } {
  const file = run.fileName;

  return run.progress.phase === "verifying"
    ? {
        title: <Trans>Leave while {file} is checked?</Trans>,
        confirmLabel: <Trans>Leave page</Trans>,
      }
    : {
        title: <Trans>Stop the upload of {file}?</Trans>,
        confirmLabel: <Trans>Stop upload and leave</Trans>,
      };
}

function leaveWhileUploading(run: UploadRun): string {
  const file = run.fileName;
  const percent = percentOf(run.progress.offset, run.progress.length);

  return t`Choose ${file} again to resume at ${percent}%.`;
}
