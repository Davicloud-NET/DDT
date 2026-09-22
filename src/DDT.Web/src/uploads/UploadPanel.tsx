// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useBlocker, type ShouldBlockFn } from "@tanstack/react-router";
import { useEffect, useId, useRef, useState, type ReactNode } from "react";

import { ConfirmDialog } from "@/components/ConfirmDialog";
import {
  discardUpload,
  uploadsQuery,
  type ImageUploadSession,
  type UploadKind,
} from "@/images/images";
import { formatBytes, formatDuration, percentOf } from "@/lib/format";
import { useNow } from "@/lib/useNow";

import { resumableUpload, type UploadOutcome, type UploadProgress } from "./resumableUpload";

import styles from "./UploadPanel.module.scss";

interface Run {
  fileName: string;
  sessionId: string | null;
  startedAt: number;
  progress: UploadProgress;
}

interface Outcome {
  failed: boolean;
  message: string;
}

export interface UploadPanelProps<T> {
  // What the next file becomes.
  kind: UploadKind;
  // The kinds of unfinished uploads this panel offers to resume or discard.
  kinds: readonly UploadKind[];
  fileLabel: string;
  accept: string;
  hint: string;
  // What the server does while it checks a complete file, and what leaving then means.
  verifyingHint: string;
  leaveWhileVerifying: (fileName: string) => string;
  // How an unfinished upload is resumed, such as "Select install.wim again to resume (25%)."
  resumeText: (session: ImageUploadSession) => string;
  describeResult: (fileName: string, outcome: UploadOutcome, library: T) => string;
  // The library the file went into has changed.
  onAdded: () => void;
  // Choices that belong to the next file, such as the kind of a package.
  children?: ReactNode;
}

// Signing out ends the session the upload needs, so it is never held up. Other navigation inside the app
// asks first.
const leavesWithoutSigningOut: ShouldBlockFn = ({ next }) => next.routeId !== "/sign-in";

// Uploads a file in slices the server acknowledges one by one, so a dropped connection or a reload loses at
// most one slice. The file has to be selected again after a reload, because the page cannot keep it.
export function UploadPanel<T>({
  kind,
  kinds,
  fileLabel,
  accept,
  hint,
  verifyingHint,
  leaveWhileVerifying,
  resumeText,
  describeResult,
  onAdded,
  children,
}: UploadPanelProps<T>) {
  const queryClient = useQueryClient();
  const uploads = useQuery(uploadsQuery);
  const inputId = useId();

  const [run, setRun] = useState<Run | null>(null);
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [discardTarget, setDiscardTarget] = useState<ImageUploadSession | null>(null);
  const controller = useRef<AbortController | null>(null);

  const uploading = run !== null;

  // The elapsed time counts seconds while a file uploads; otherwise nothing here shows the time.
  const now = useNow(uploading ? 1_000 : 60_000);

  // A link to another page would unmount this panel and stop the upload, so it asks first. Reloads and
  // closing the tab are asked about by the beforeunload listener below.
  const leaving = useBlocker({
    shouldBlockFn: leavesWithoutSigningOut,
    enableBeforeUnload: false,
    disabled: !uploading,
    withResolver: true,
  });

  const discard = useMutation({
    mutationFn: (id: string) => discardUpload(id),
    onSuccess: () => {
      setDiscardTarget(null);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: uploadsQuery.queryKey });
    },
  });

  // Leaving the page, once confirmed, stops the transfer. The server keeps what it has for the next
  // selection of the file.
  useEffect(() => {
    return () => {
      controller.current?.abort();
    };
  }, []);

  // An upload that ends while the question is open leaves nothing to lose, and staying shows its result.
  useEffect(() => {
    if (!uploading && leaving.status === "blocked") {
      leaving.reset();
    }
  }, [uploading, leaving]);

  useEffect(() => {
    if (!uploading) {
      return;
    }

    const warn = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };

    window.addEventListener("beforeunload", warn);

    return () => {
      window.removeEventListener("beforeunload", warn);
    };
  }, [uploading]);

  function start(file: File) {
    setOutcome(null);

    if (file.size === 0) {
      setOutcome({ failed: true, message: `${file.name} is empty.` });
      return;
    }

    const current = new AbortController();
    controller.current = current;

    setRun({
      fileName: file.name,
      sessionId: null,
      startedAt: Date.now(),
      progress: {
        phase: "uploading",
        offset: 0,
        length: file.size,
        sentBytes: 0,
        retrying: false,
      },
    });

    void resumableUpload<T>(file, kind, {
      signal: current.signal,
      onSession: (session) => {
        setRun((previous) => previous && { ...previous, sessionId: session.id });
      },
      onProgress: (progress) => {
        setRun((previous) => previous && { ...previous, progress });
      },
    })
      .then(
        (result) => {
          setOutcome({
            failed: false,
            message: describeResult(file.name, result.outcome, result.library),
          });
          onAdded();
        },
        (error: unknown) => {
          setOutcome(
            current.signal.aborted
              ? {
                  failed: false,
                  message: `The upload of ${file.name} stopped. Select the file again to resume.`,
                }
              : {
                  failed: true,
                  message: `${file.name} was not added. ${error instanceof Error ? error.message : "The upload failed."}`,
                },
          );
        },
      )
      .finally(() => {
        if (controller.current === current) {
          controller.current = null;
        }

        setRun(null);
        void queryClient.invalidateQueries({ queryKey: uploadsQuery.queryKey });
      });
  }

  const open = (uploads.data ?? []).filter(
    (session) => session.id !== run?.sessionId && kinds.includes(session.kind ?? "Image"),
  );

  return (
    <section className={styles.panel} aria-labelledby={`${inputId}-title`}>
      <h2 id={`${inputId}-title`} className={styles.title}>
        Upload
      </h2>

      {children}

      <div className={styles.field}>
        <label htmlFor={inputId}>{fileLabel}</label>
        <input
          id={inputId}
          type="file"
          accept={accept}
          disabled={uploading}
          onChange={(event) => {
            const file = event.target.files?.[0];

            // Cleared so that selecting the same file again, to resume it, counts as a change.
            event.target.value = "";

            if (file !== undefined) {
              start(file);
            }
          }}
        />
        <span className={styles.hint}>{hint}</span>
      </div>

      {run !== null && (
        <div className={styles.progress}>
          {renderProgress(run, now, verifyingHint)}
          {/* While verifying, the server goes on checking the file whatever this page does. */}
          {run.progress.phase === "uploading" && (
            <div>
              <button
                type="button"
                className={styles.secondary}
                onClick={() => {
                  controller.current?.abort();
                }}
              >
                Stop upload
              </button>
            </div>
          )}
        </div>
      )}

      {leaving.status === "blocked" && run !== null && (
        <ConfirmDialog
          open
          onOpenChange={(next) => {
            if (!next) {
              leaving.reset();
            }
          }}
          {...leaveQuestion(run, leaveWhileVerifying)}
          busy={false}
          error={null}
          onConfirm={() => {
            leaving.proceed();
          }}
        />
      )}

      {outcome !== null && (
        <p
          className={outcome.failed ? styles.error : styles.done}
          role={outcome.failed ? "alert" : "status"}
        >
          {outcome.message}
        </p>
      )}

      {open.length > 0 && (
        <div className={styles.sessions}>
          <h3 className={styles.title}>Unfinished uploads</h3>
          <ul className={styles.sessionList}>
            {open.map((session) => (
              <li key={session.id} className={styles.session}>
                <span>{resumeText(session)}</span>
                <button
                  type="button"
                  className={styles.secondary}
                  aria-label={`Discard ${session.fileName}`}
                  onClick={() => {
                    discard.reset();
                    setDiscardTarget(session);
                  }}
                >
                  Discard
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}

      {discardTarget !== null && (
        <ConfirmDialog
          open
          onOpenChange={(next) => {
            if (!next) {
              setDiscardTarget(null);
            }
          }}
          title={`Discard the upload of ${discardTarget.fileName}?`}
          consequence={`The ${formatBytes(discardTarget.offset)} of ${discardTarget.fileName} uploaded so far are deleted from the server. Selecting the file again starts the upload over.`}
          confirmLabel="Discard upload"
          busy={discard.isPending}
          error={discard.isError ? discard.error.message : null}
          onConfirm={() => {
            discard.mutate(discardTarget.id);
          }}
        />
      )}
    </section>
  );
}

function renderProgress(run: Run, now: number, verifyingHint: string): ReactNode {
  const { progress } = run;
  const percent = percentOf(progress.offset, progress.length);
  const elapsed = Math.max(0, now - run.startedAt);
  const verifying = progress.phase === "verifying";
  const facts = [`${String(percent)}% of ${formatBytes(progress.length)}`];

  // The average of this run, which is steadier than the last slice.
  if (!verifying && elapsed >= 1_000) {
    facts.push(`${formatBytes((progress.sentBytes * 1000) / elapsed)}/s`);
  }

  facts.push(`${formatDuration(elapsed)} elapsed`);

  return (
    <>
      <p className={styles.status}>
        {verifying ? `Verifying ${run.fileName}` : `Uploading ${run.fileName}`}
      </p>
      <progress
        className={styles.bar}
        max={100}
        value={percent}
        aria-label={`Upload of ${run.fileName}`}
      />
      <p className={styles.hint}>{facts.join(", ")}.</p>
      {verifying && <p className={styles.hint}>{verifyingHint}</p>}
      {progress.retrying && (
        <p className={styles.hint} role="status">
          The server did not answer. The upload continues when it does.
        </p>
      )}
    </>
  );
}

// What leaving the page means at this point of the upload.
function leaveQuestion(
  run: Run,
  leaveWhileVerifying: (fileName: string) => string,
): { title: string; consequence: string; confirmLabel: string } {
  if (run.progress.phase === "verifying") {
    return {
      title: `Leave while ${run.fileName} is checked?`,
      consequence: leaveWhileVerifying(run.fileName),
      confirmLabel: "Leave page",
    };
  }

  const percent = percentOf(run.progress.offset, run.progress.length);

  return {
    title: `Stop the upload of ${run.fileName}?`,
    consequence: `Leaving this page stops the upload of ${run.fileName} at ${String(percent)}%. The server keeps what it has received, and selecting the file again on this page resumes the upload from there.`,
    confirmLabel: "Stop upload and leave",
  };
}
