// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";

import { uploadsQuery, type UploadKind } from "@/images/images";

import { resumableUpload, type UploadOutcome, type UploadProgress } from "./resumableUpload";

// A file on its way to the server.
export interface UploadRun {
  fileName: string;
  sessionId: string | null;
  startedAt: number;
  progress: UploadProgress;
}

// How the last upload ended.
export interface UploadNotice {
  failed: boolean;
  message: string;
}

export interface ResumableUploadOptions<T> {
  kind: UploadKind;
  describeResult: (fileName: string, outcome: UploadOutcome, library: T) => string;
  onAdded: (library: T) => void;
}

// Uploads one file at a time and says how it ended.
export function useResumableUpload<T>({
  kind,
  describeResult,
  onAdded,
}: ResumableUploadOptions<T>) {
  const queryClient = useQueryClient();
  const [run, setRun] = useState<UploadRun | null>(null);
  const [outcome, setOutcome] = useState<UploadNotice | null>(null);
  const controller = useRef<AbortController | null>(null);

  // Leaving the page, once confirmed, stops the transfer. The server keeps what it has for the next selection of
  // the file.
  useEffect(() => {
    return () => {
      controller.current?.abort();
    };
  }, []);

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
          setOutcome(failureNotice(name, error, current.signal.aborted));
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

  return {
    run,
    outcome,
    // A file chosen while another uploads is ignored.
    pick: (file: File) => {
      if (run === null) {
        start(file);
      }
    },
    stop: () => {
      controller.current?.abort();
    },
  };
}

function failureNotice(name: string, error: unknown, stopped: boolean): UploadNotice {
  const reason = error instanceof Error ? error.message : t`The upload failed.`;

  return stopped
    ? { failed: false, message: t`The upload of ${name} stopped. Choose the file again to resume.` }
    : { failed: true, message: t`${name} was not added. ${reason}` };
}
