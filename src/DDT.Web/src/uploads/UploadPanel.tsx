// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ReactNode } from "react";

import {
  discardUpload,
  uploadsQuery,
  type ImageUploadSession,
  type UploadKind,
} from "@/images/images";
import { removeByIds } from "@/lib/listCache";
import { useNow } from "@/lib/useNow";
import { useDeletion } from "@/library/useDeletion";
import { FileDropZone } from "@/ui/FileDropZone";
import { Notice } from "@/ui/Notice";

import { DiscardUploadDialog } from "./DiscardUploadDialog";
import { LeaveUploadDialog } from "./LeaveUploadDialog";
import type { UploadOutcome } from "./resumableUpload";
import { UnfinishedUploads } from "./UnfinishedUploads";
import { UploadProgress } from "./UploadProgress";
import { useLeaveGuard } from "./useLeaveGuard";
import { useResumableUpload } from "./useResumableUpload";

export interface UploadPanelProps<T> {
  // What the next file becomes.
  kind: UploadKind;
  // The kinds of unfinished uploads this panel offers to resume or discard.
  kinds: readonly UploadKind[];
  // What to drop or choose, such as "a WIM, ESD or disk image file".
  what: ReactNode;
  accept: readonly string[];
  hint: ReactNode;
  // What the server does while it checks a complete file, and what leaving then means.
  verifyingHint: ReactNode;
  leaveWhileVerifying: (fileName: string) => string;
  describeResult: (fileName: string, outcome: UploadOutcome, library: T) => string;
  // Called when the file went into the library, with the server's answer.
  onAdded: (library: T) => void;
  // Choices that belong to the next file, such as the kind of a package.
  children?: ReactNode;
}

// Uploads a file in slices the server acknowledges one by one, so a dropped connection or a reload loses at most one
// slice. The page can't keep the file across a reload, so the unfinished uploads say which file to choose again.
export function UploadPanel<T>({
  kind,
  kinds,
  what,
  accept,
  hint,
  verifyingHint,
  leaveWhileVerifying,
  describeResult,
  onAdded,
  children,
}: UploadPanelProps<T>) {
  const { t: translate } = useLingui();
  const queryClient = useQueryClient();
  const uploads = useQuery(uploadsQuery);
  const upload = useResumableUpload<T>({ kind, describeResult, onAdded });
  const { run } = upload;
  // The elapsed time ticks every second while a file uploads. Otherwise nothing here shows the time.
  const now = useNow(run !== null ? 1_000 : 60_000);
  const leaving = useLeaveGuard(run !== null);
  const discard = useDeletion<ImageUploadSession>(discardUpload, (id) => {
    queryClient.setQueryData(uploadsQuery.queryKey, (sessions) => removeByIds(sessions, [id]));
  });

  const open = (uploads.data ?? []).filter(
    (session) => session.id !== run?.sessionId && kinds.includes(session.kind ?? "Image"),
  );

  return (
    <section
      aria-label={translate`Upload`}
      className="flex flex-col gap-3 rounded-panel bg-panel p-4 shadow-panel"
    >
      {children}

      {run === null ? (
        <FileDropZone
          label={translate`Drop a file to upload`}
          title={<Trans>Drop {what} here, or choose one.</Trans>}
          hint={hint}
          accept={accept}
          onFile={upload.pick}
        />
      ) : (
        <UploadProgress run={run} now={now} verifyingHint={verifyingHint} onStop={upload.stop} />
      )}

      {upload.outcome !== null ? (
        <Notice tone={upload.outcome.failed ? "fail" : "info"}>{upload.outcome.message}</Notice>
      ) : null}

      {open.length > 0 ? <UnfinishedUploads sessions={open} onDiscard={discard.ask} /> : null}

      <LeaveUploadDialog leaving={leaving} run={run} leaveWhileVerifying={leaveWhileVerifying} />

      <DiscardUploadDialog discard={discard} />
    </section>
  );
}
