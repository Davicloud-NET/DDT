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
  leaveWhileVerifying,
}: {
  leaving: LeaveGuard;
  run: UploadRun | null;
  leaveWhileVerifying: (fileName: string) => string;
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
            ? leaveWhileVerifying(run.fileName)
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

  return t`Leaving this page stops the upload of ${file} at ${percent}%. The server keeps what it has received, and choosing the file again here resumes the upload from there.`;
}
