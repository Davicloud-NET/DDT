// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { formatBytes, formatDuration, percentOf } from "@/lib/format";
import { Button } from "@/ui/Button";
import { ProgressBar } from "@/ui/ProgressBar";

import type { UploadRun } from "./useResumableUpload";

// How far a file's upload or the server's check of it got, with the key that stops the upload.
export function UploadProgress({
  run,
  now,
  verifyingHint,
  onStop,
}: {
  run: UploadRun;
  now: number;
  verifyingHint: ReactNode;
  onStop: () => void;
}) {
  const { progress } = run;
  const percent = percentOf(progress.offset, progress.length);
  const elapsed = Math.max(0, now - run.startedAt);
  const verifying = progress.phase === "verifying";
  const file = run.fileName;
  const total = formatBytes(progress.length);
  const took = formatDuration(elapsed);
  // The average of this run, which is steadier than the last slice.
  const speed =
    !verifying && elapsed >= 1_000 ? formatBytes((progress.sentBytes * 1000) / elapsed) : null;

  return (
    <div className="flex flex-col gap-2">
      <ProgressBar
        label={verifying ? <Trans>Checking {file}</Trans> : <Trans>Uploading {file}</Trans>}
        value={percent}
      />
      <span className="flex flex-wrap items-center gap-3 type-small text-muted">
        <span className="flex-1">
          {speed === null
            ? t`${percent}% of ${total}, ${took} so far`
            : t`${percent}% of ${total} at ${speed}/s, ${took} so far`}
        </span>
        {verifying ? null : (
          <Button size="sm" onPress={onStop}>
            <Trans>Stop upload</Trans>
          </Button>
        )}
      </span>
      {verifying ? <span className="type-small text-ink-2">{verifyingHint}</span> : null}
      {progress.retrying ? (
        <span role="status" className="type-small text-attention-text">
          <Trans>The server does not answer. The upload goes on when it does.</Trans>
        </span>
      ) : null}
    </div>
  );
}
