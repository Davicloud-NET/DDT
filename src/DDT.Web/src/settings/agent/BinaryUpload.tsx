// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { ConfirmDialog } from "@/ui/ConfirmDialog";
import { FileDropZone } from "@/ui/FileDropZone";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { ProgressBar } from "@/ui/ProgressBar";

import { ReauthDialog } from "../parts/ReauthDialog";

import type { Binary } from "./binary";
import { useBinaryUpload } from "./useBinaryUpload";

export function BinaryUpload({ binary }: { binary: Binary }) {
  const upload = useBinaryUpload(binary);
  const action = upload.action;
  const name = upload.name;
  const reason = action.error;

  return (
    <Panel title={binary.uploadTitle}>
      <Notice tone="attention">{binary.warning}</Notice>
      <p className="max-w-[80ch] text-ink-2">{binary.explanation}</p>

      {action.busy ? (
        <ProgressBar label={<Trans>Uploading {name}</Trans>} />
      ) : (
        <FileDropZone
          label={binary.dropLabel}
          title={binary.dropHere}
          hint={binary.dropHint}
          accept={binary.accept}
          onFile={upload.pick}
        />
      )}

      {upload.problem !== null ? <Notice tone="fail">{upload.problem}</Notice> : null}
      {reason !== null ? (
        <Notice tone="fail">
          <Trans>
            {name} was not uploaded. {reason}
          </Trans>
        </Notice>
      ) : null}
      {upload.uploaded !== null ? <Notice>{binary.uploaded(upload.uploaded)}</Notice> : null}

      <ConfirmDialog
        isOpen={upload.confirming}
        onOpenChange={(open) => {
          if (!open) {
            upload.dismiss();
          }
        }}
        title={binary.confirmTitle(name)}
        confirmLabel={binary.confirmLabel}
        onConfirm={upload.confirm}
      >
        <p>{binary.confirmBody(name, upload.size)}</p>
      </ConfirmDialog>

      <ReauthDialog
        isOpen={action.needsReauth}
        onAccepted={action.retryAfterReauth}
        onCancel={upload.cancelReauth}
        confirmLabel={<Trans>Confirm and upload</Trans>}
        reason={binary.reauthReason}
      />
    </Panel>
  );
}
