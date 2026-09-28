// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { CreateImageUploadRequest, ImageUploadSession, UploadKind } from "@/images/images";
import { apiErrorFrom, apiFetch, apiPatch } from "@/lib/api";
import { backoff } from "@/lib/backoff";

import { throwIfAborted, waitFor } from "./abortableWait";
import { isTransient, readOffset, refusal, retryAfter } from "./uploadAnswers";

export type UploadPhase = "uploading" | "verifying";

export interface UploadProgress {
  phase: UploadPhase;
  // Bytes the server has stored so far.
  offset: number;
  length: number;
  // Bytes this run sent and the server accepted, for the speed.
  sentBytes: number;
  // True while waiting to send again after a failed request.
  retrying: boolean;
}

// "added": this upload put the file's contents into the library. "duplicate": they were already in the
// library. "unclear": they're in the library, but an earlier complete request whose answer never arrived may
// have added them. The server answers a repeated complete the same way as a duplicate.
export type UploadOutcome = "added" | "duplicate" | "unclear";

// library is the server's answer to the completion: the images of a WIM, or the package of a zip.
export interface ResumableResult<T> {
  outcome: UploadOutcome;
  library: T;
}

export interface UploadOptions {
  signal: AbortSignal;
  onSession?: (session: ImageUploadSession) => void;
  onProgress?: (progress: UploadProgress) => void;
  // Replaced in tests, so the back off doesn't really wait.
  wait?: (milliseconds: number, signal: AbortSignal) => Promise<void>;
}

// Consecutive failed requests after which the upload gives up. With the back off doubling up to 30 s
// this is about two and a half minutes without an answer.
export const MAX_FAILURES = 10;

const DEFAULT_CHUNK_BYTES = 8 * 1024 * 1024;

// One upload's state, which its requests share.
interface Transfer {
  readonly file: File;
  readonly options: UploadOptions;
  readonly signal: AbortSignal;
  // The pause before a request is sent again. The abort ends it.
  readonly pause: (milliseconds: number) => Promise<void>;
  phase: UploadPhase;
  offset: number;
  sentBytes: number;
  retrying: boolean;
  // Failed requests in a row.
  failures: number;
  completeRequests: number;
}

// The server keeps the offset it has stored. Every request says where its bytes start, and a refusal says
// where the server is. That covers a lost answer, a second tab and a reload. The server recognises the file
// by its name, length, modification time and kind.
export async function resumableUpload<T>(
  file: File,
  kind: UploadKind,
  options: UploadOptions,
): Promise<ResumableResult<T>> {
  const { signal } = options;
  const wait = options.wait ?? waitFor;
  const transfer: Transfer = {
    file,
    options,
    signal,
    pause: (milliseconds) => wait(milliseconds, signal),
    phase: "uploading",
    offset: 0,
    sentBytes: 0,
    retrying: false,
    failures: 0,
    completeRequests: 0,
  };

  const session = await createSession(transfer, kind);
  const path = `/api/images/uploads/${session.id}`;
  const chunkBytes = session.chunkBytes > 0 ? session.chunkBytes : DEFAULT_CHUNK_BYTES;

  options.onSession?.(session);
  transfer.offset = session.offset;
  report(transfer);

  for (;;) {
    await sendSlices(transfer, path, chunkBytes);

    const result = await complete<T>(transfer, path);

    if (result !== null) {
      return result;
    }
  }
}

function report(transfer: Transfer): void {
  const { phase, offset, file, sentBytes, retrying } = transfer;

  transfer.options.onProgress?.({ phase, offset, length: file.size, sentBytes, retrying });
}

// Network errors and transient statuses are sent again after a pause. Anything else is an answer.
async function send(transfer: Transfer, attempt: () => Promise<Response>): Promise<Response> {
  for (;;) {
    throwIfAborted(transfer.signal);

    let response: Response | null = null;

    try {
      response = await attempt();
    } catch {
      // A network error, or the abort. The check below turns the abort into the abort error.
      throwIfAborted(transfer.signal);
    }

    if (response !== null && !isTransient(response.status)) {
      transfer.failures = 0;

      if (transfer.retrying) {
        transfer.retrying = false;
        report(transfer);
      }

      return response;
    }

    transfer.failures++;

    if (transfer.failures >= MAX_FAILURES) {
      throw new Error(
        `The server did not take the upload after ${String(MAX_FAILURES)} attempts. Select the file again to resume.`,
      );
    }

    transfer.retrying = true;
    report(transfer);
    await transfer.pause(
      (response === null ? null : retryAfter(response)) ?? backoff(transfer.failures),
    );
  }
}

async function createSession(transfer: Transfer, kind: UploadKind): Promise<ImageUploadSession> {
  const { file, signal } = transfer;
  // The server reads an upload without a kind as an image.
  const request: CreateImageUploadRequest = {
    fileName: file.name,
    length: file.size,
    lastModified: file.lastModified,
    ...(kind === "Image" ? {} : { kind }),
  };

  const created = await send(transfer, () =>
    apiFetch("/api/images/uploads", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
      signal,
    }),
  );

  if (!created.ok) {
    throw await apiErrorFrom(created);
  }

  return (await created.json()) as ImageUploadSession;
}

async function sendSlices(transfer: Transfer, path: string, chunkBytes: number): Promise<void> {
  const { file, signal } = transfer;

  transfer.phase = "uploading";

  while (transfer.offset < file.size) {
    const start = transfer.offset;
    const end = Math.min(start + chunkBytes, file.size);
    const response = await send(transfer, () =>
      apiPatch(path, file.slice(start, end), {
        headers: { "Upload-Offset": String(start) },
        signal,
      }),
    );

    if (response.ok) {
      transfer.offset = readOffset(response, file.size) ?? end;
      transfer.sentBytes += Math.max(0, transfer.offset - start);
    } else if (response.status === 409) {
      await followConflict(transfer, response, start);
    } else {
      throw await refusal(response);
    }

    report(transfer);
  }
}

// Null when the server holds fewer bytes than the file, or is still busy with it. Then the upload continues.
async function complete<T>(transfer: Transfer, path: string): Promise<ResumableResult<T> | null> {
  transfer.phase = "verifying";
  report(transfer);

  const response = await send(transfer, () => {
    transfer.completeRequests++;
    return apiFetch(`${path}/complete`, { method: "POST", signal: transfer.signal });
  });

  if (response.status === 200 || response.status === 201) {
    return {
      // Only the request that did the work gets a 201. A 200 only means a duplicate when no complete request
      // came before it.
      outcome:
        response.status === 201
          ? "added"
          : transfer.completeRequests === 1
            ? "duplicate"
            : "unclear",
      library: (await response.json()) as T,
    };
  }

  if (response.status !== 409) {
    throw await refusal(response);
  }

  await followConflict(transfer, response, transfer.file.size);

  return null;
}

// A 409 either names a different offset the server has stored, or asks to try again later because another
// request holds the session. A busy answer to a chunk also names an offset, and it's the one just sent.
async function followConflict(transfer: Transfer, response: Response, sent: number): Promise<void> {
  const moved = readOffset(response, transfer.file.size);

  if (moved !== null && moved !== sent) {
    transfer.offset = moved;
    return;
  }

  const delay = retryAfter(response);

  // The offset did not move, so a Retry-After means the server is busy with this upload.
  if (delay !== null) {
    await transfer.pause(delay);
    return;
  }

  throw await apiErrorFrom(response);
}
