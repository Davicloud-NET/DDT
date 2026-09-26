// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconUpload } from "@tabler/icons-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useBlocker, type ShouldBlockFn } from "@tanstack/react-router";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { DropZone, FileTrigger, Text, type FileDropItem } from "react-aria-components";

import {
  discardUpload,
  uploadsQuery,
  type ImageUploadSession,
  type UploadKind,
} from "@/images/images";
import { formatBytes, formatDuration, percentOf } from "@/lib/format";
import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { ProgressBar } from "@/ui/Controls";
import { cx } from "@/ui/cx";
import { ConfirmDialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";

import { resumableUpload, type UploadOutcome, type UploadProgress } from "./resumableUpload";

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
  // What to drop or choose, such as "a WIM, ESD or disk image file".
  what: ReactNode;
  accept: readonly string[];
  hint: ReactNode;
  // What the server does while it checks a complete file, and what leaving then means.
  verifyingHint: ReactNode;
  leaveWhileVerifying: (fileName: string) => string;
  describeResult: (fileName: string, outcome: UploadOutcome, library: T) => string;
  // The library the file went into has changed, with what the server answered.
  onAdded: (library: T) => void;
  // Choices that belong to the next file, such as the kind of a package.
  children?: ReactNode;
}

// Signing out ends the session the upload needs, so it is never held up. Other navigation inside the app asks
// first.
const leavesWithoutSigningOut: ShouldBlockFn = ({ next }) => next.routeId !== "/sign-in";

// Uploads a file in slices the server acknowledges one by one, so a dropped connection or a reload loses at most one
// slice. The file has to be chosen again after a reload, because the page cannot keep it; the unfinished uploads
// below say which, and how far each got.
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
  const [run, setRun] = useState<Run | null>(null);
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [discarding, setDiscarding] = useState<ImageUploadSession | null>(null);
  const controller = useRef<AbortController | null>(null);
  const uploading = run !== null;

  // The elapsed time counts seconds while a file uploads; otherwise nothing here shows the time.
  const now = useNow(uploading ? 1_000 : 60_000);

  // A link to another page would unmount this panel and stop the upload, so it asks first. Reloads and closing the
  // tab are asked about by the beforeunload listener below.
  const leaving = useBlocker({
    shouldBlockFn: leavesWithoutSigningOut,
    enableBeforeUnload: false,
    disabled: !uploading,
    withResolver: true,
  });

  const discard = useMutation({
    mutationFn: (id: string) => discardUpload(id),
    onSuccess: (_, id) => {
      queryClient.setQueryData(uploadsQuery.queryKey, (sessions) =>
        sessions?.filter((session) => session.id !== id),
      );
      setDiscarding(null);
    },
  });

  // Leaving the page, once confirmed, stops the transfer. The server keeps what it has for the next selection of
  // the file.
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
    const name = file.name;
    setOutcome(null);

    if (file.size === 0) {
      setOutcome({ failed: true, message: t`${name} is empty.` });
      return;
    }

    const current = new AbortController();
    controller.current = current;

    setRun({
      fileName: name,
      sessionId: null,
      startedAt: Date.now(),
      progress: { phase: "uploading", offset: 0, length: file.size, sentBytes: 0, retrying: false },
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
            message: describeResult(name, result.outcome, result.library),
          });
          onAdded(result.library);
        },
        (error: unknown) => {
          const reason = error instanceof Error ? error.message : t`The upload failed.`;

          setOutcome(
            current.signal.aborted
              ? {
                  failed: false,
                  message: t`The upload of ${name} stopped. Choose the file again to resume.`,
                }
              : { failed: true, message: t`${name} was not added. ${reason}` },
          );
        },
      )
      .finally(() => {
        if (controller.current === current) {
          controller.current = null;
        }

        setRun(null);
        // The session list is the server's; the one this upload used is gone or finished now.
        void queryClient.invalidateQueries({ queryKey: uploadsQuery.queryKey });
      });
  }

  const open = (uploads.data ?? []).filter(
    (session) => session.id !== run?.sessionId && kinds.includes(session.kind ?? "Image"),
  );
  const pick = (files: File[]) => {
    const [file] = files;

    if (file !== undefined && !uploading) {
      start(file);
    }
  };

  return (
    <section
      aria-label={translate`Upload`}
      className="flex flex-col gap-3 rounded-panel bg-panel p-4 shadow-panel"
    >
      {children}

      {run === null ? (
        <DropZone
          aria-label={translate`Drop a file to upload`}
          onDrop={(event) => {
            const item = event.items.find(
              (candidate): candidate is FileDropItem => candidate.kind === "file",
            );

            if (item !== undefined) {
              void item.getFile().then((file) => {
                pick([file]);
              });
            }
          }}
          className={({ isDropTarget }) =>
            cx(
              "flex flex-wrap items-center gap-x-4 gap-y-2 rounded-key bg-well px-4 py-3.5 shadow-[inset_0_0_0_1px_var(--color-line)] outline-none",
              isDropTarget && "shadow-[inset_0_0_0_2px_var(--color-focus)]",
            )
          }
        >
          <IconUpload aria-hidden="true" size={22} stroke={1.75} className="text-muted" />
          <span className="flex min-w-0 flex-1 flex-col gap-0.5">
            <Text slot="label" className="type-label text-ink">
              <Trans>Drop {what} here, or choose one.</Trans>
            </Text>
            <span className="type-small text-muted">{hint}</span>
          </span>
          <FileTrigger
            acceptedFileTypes={[...accept]}
            onSelect={(list) => {
              pick(list === null ? [] : Array.from(list));
            }}
          >
            <Button>
              <Trans>Choose a file</Trans>
            </Button>
          </FileTrigger>
        </DropZone>
      ) : (
        <Progress
          run={run}
          now={now}
          verifyingHint={verifyingHint}
          onStop={() => {
            controller.current?.abort();
          }}
        />
      )}

      {outcome !== null ? (
        <Notice tone={outcome.failed ? "fail" : "info"}>
          <span role={outcome.failed ? "alert" : "status"}>{outcome.message}</span>
        </Notice>
      ) : null}

      {open.length > 0 ? (
        <div className="flex flex-col gap-1.5">
          <span className="type-label text-ink">
            <Trans>Unfinished uploads</Trans>
          </span>
          <ul className="flex flex-col">
            {open.map((session) => {
              const file = session.fileName;
              const percent = percentOf(session.offset, session.length);

              return (
                <li
                  key={session.id}
                  className="flex flex-wrap items-center gap-3 border-t border-line-soft py-2 first:border-t-0"
                >
                  <span className="min-w-0 flex-1 type-small text-ink-2">
                    <Trans>
                      Choose {file} again to resume at {percent}%.
                    </Trans>
                  </span>
                  <Button
                    size="sm"
                    variant="quiet"
                    aria-label={translate`Discard ${file}`}
                    onPress={() => {
                      discard.reset();
                      setDiscarding(session);
                    }}
                  >
                    <Trans>Discard</Trans>
                  </Button>
                </li>
              );
            })}
          </ul>
        </div>
      ) : null}

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

      <ConfirmDialog
        isOpen={discarding !== null}
        onOpenChange={(next) => {
          if (!next) {
            setDiscarding(null);
          }
        }}
        title={discarding === null ? "" : <DiscardTitle file={discarding.fileName} />}
        confirmLabel={<Trans>Discard upload</Trans>}
        danger
        isBusy={discard.isPending}
        error={discard.isError ? discard.error.message : undefined}
        onConfirm={() => {
          if (discarding !== null) {
            discard.mutate(discarding.id);
          }
        }}
      >
        <p>{discarding === null ? null : discardConsequence(discarding)}</p>
      </ConfirmDialog>
    </section>
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

function Progress({
  run,
  now,
  verifyingHint,
  onStop,
}: {
  run: Run;
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

// What leaving the page means at this point of the upload.
function leaveQuestion(run: Run): { title: ReactNode; confirmLabel: ReactNode } {
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

function leaveWhileUploading(run: Run): string {
  const file = run.fileName;
  const percent = percentOf(run.progress.offset, run.progress.length);

  return t`Leaving this page stops the upload of ${file} at ${percent}%. The server keeps what it has received, and choosing the file again here resumes the upload from there.`;
}
