// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { CreateImageUploadRequest, ImageUploadSession, UploadKind } from "@/images/images";
import { ApiError, apiErrorFrom, apiFetch, apiPatch } from "@/lib/api";

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

// "added": this upload put the file's contents into the library. "duplicate": they were in the library
// already. "unclear": they are in the library, but an earlier complete request whose answer never arrived may
// have added them, and the server answers a repeated complete as it answers a duplicate.
export type UploadOutcome = "added" | "duplicate" | "unclear";

// library is what the server answers the completion with: the images of a WIM, or the package of a zip.
export interface ResumableResult<T> {
  outcome: UploadOutcome;
  library: T;
}

export interface UploadOptions {
  signal: AbortSignal;
  onSession?: (session: ImageUploadSession) => void;
  onProgress?: (progress: UploadProgress) => void;
  // Replaced in tests, so the back off does not wait for real.
  wait?: (milliseconds: number, signal: AbortSignal) => Promise<void>;
}

// Consecutive failed requests after which the upload gives up. With the back off doubling up to 30 s
// this is about two and a half minutes without an answer.
export const MAX_FAILURES = 10;

const DEFAULT_CHUNK_BYTES = 8 * 1024 * 1024;
const MAX_WAIT = 60_000;

// The server keeps the offset it has stored, so every request says where its bytes start, and a refusal
// says where the server stands. That covers a lost answer, a second tab and a reload, which the server
// recognises by the file's name, length, modification time and kind.
export async function resumableUpload<T>(
  file: File,
  kind: UploadKind,
  options: UploadOptions,
): Promise<ResumableResult<T>> {
  const { signal } = options;
  const wait = options.wait ?? waitFor;

  let phase: UploadPhase = "uploading";
  let offset = 0;
  let sentBytes = 0;
  let retrying = false;
  let failures = 0;
  let completeRequests = 0;

  const report = () => {
    options.onProgress?.({ phase, offset, length: file.size, sentBytes, retrying });
  };

  // Network errors and transient statuses are sent again after a pause. Anything else is an answer.
  const send = async (attempt: () => Promise<Response>): Promise<Response> => {
    for (;;) {
      throwIfAborted(signal);

      let response: Response | null = null;

      try {
        response = await attempt();
      } catch {
        // A network error, or the abort, which the check below turns into the abort error.
        throwIfAborted(signal);
      }

      if (response !== null && !isTransient(response.status)) {
        failures = 0;

        if (retrying) {
          retrying = false;
          report();
        }

        return response;
      }

      failures++;

      if (failures >= MAX_FAILURES) {
        throw new Error(
          `The server did not take the upload after ${String(MAX_FAILURES)} attempts. Select the file again to resume.`,
        );
      }

      retrying = true;
      report();
      await wait((response === null ? null : retryAfter(response)) ?? backoff(failures), signal);
    }
  };

  // The server reads an upload without a kind as an image.
  const request: CreateImageUploadRequest = {
    fileName: file.name,
    length: file.size,
    lastModified: file.lastModified,
    ...(kind === "Image" ? {} : { kind }),
  };

  const created = await send(() =>
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

  const session = (await created.json()) as ImageUploadSession;
  const path = `/api/images/uploads/${session.id}`;
  const chunkBytes = session.chunkBytes > 0 ? session.chunkBytes : DEFAULT_CHUNK_BYTES;

  options.onSession?.(session);
  offset = session.offset;
  report();

  for (;;) {
    phase = "uploading";

    while (offset < file.size) {
      const start = offset;
      const end = Math.min(start + chunkBytes, file.size);
      const response = await send(() =>
        apiPatch(path, file.slice(start, end), {
          headers: { "Upload-Offset": String(start) },
          signal,
        }),
      );

      if (response.ok) {
        offset = readOffset(response, file.size) ?? end;
        sentBytes += Math.max(0, offset - start);
      } else if (response.status === 409) {
        await followConflict(response, start, file.size, wait, signal, (moved) => {
          offset = moved;
        });
      } else {
        throw await refusal(response);
      }

      report();
    }

    phase = "verifying";
    report();

    const response = await send(() => {
      completeRequests++;
      return apiFetch(`${path}/complete`, { method: "POST", signal });
    });

    if (response.status === 200 || response.status === 201) {
      return {
        // Only the request that did the work answers 201. A 200 means a duplicate only when no complete
        // request went before it.
        outcome:
          response.status === 201 ? "added" : completeRequests === 1 ? "duplicate" : "unclear",
        library: (await response.json()) as T,
      };
    }

    if (response.status !== 409) {
      throw await refusal(response);
    }

    // Either the server is still working on it, or it holds fewer bytes than the file has.
    await followConflict(response, file.size, file.size, wait, signal, (moved) => {
      offset = moved;
    });
  }
}

// A 409 either names another offset the server has stored, or asks to try again later because another
// request holds the session. A busy answer to a chunk names the offset too, and it is the one just sent.
async function followConflict(
  response: Response,
  sent: number,
  length: number,
  wait: (milliseconds: number, signal: AbortSignal) => Promise<void>,
  signal: AbortSignal,
  move: (offset: number) => void,
): Promise<void> {
  const moved = readOffset(response, length);

  if (moved !== null && moved !== sent) {
    move(moved);
    return;
  }

  const delay = retryAfter(response);

  // The offset did not move, so a Retry-After means the server is busy with this upload.
  if (delay !== null) {
    await wait(delay, signal);
    return;
  }

  throw await apiErrorFrom(response);
}

async function refusal(response: Response): Promise<ApiError> {
  if (response.status === 404) {
    return new ApiError(
      404,
      "The server no longer has this upload, so it was discarded. Select the file again to start over.",
    );
  }

  return apiErrorFrom(response);
}

// Answers that say "not now" rather than "no". 507, a full store, is a refusal.
const transientStatuses = [408, 429, 500, 502, 503, 504];

function isTransient(status: number): boolean {
  return transientStatuses.includes(status);
}

function readOffset(response: Response, length: number): number | null {
  const header = response.headers.get("Upload-Offset");

  if (header === null) {
    return null;
  }

  const offset = Number(header);

  if (header.trim() === "" || !Number.isSafeInteger(offset) || offset < 0 || offset > length) {
    throw new Error(`The server answered with an offset the file does not have: ${header}.`);
  }

  return offset;
}

function retryAfter(response: Response): number | null {
  const header = response.headers.get("Retry-After");

  if (header === null || header.trim() === "") {
    return null;
  }

  const seconds = Number(header);
  const milliseconds = Number.isFinite(seconds) ? seconds * 1000 : Date.parse(header) - Date.now();

  return Number.isNaN(milliseconds) ? null : Math.min(MAX_WAIT, Math.max(0, milliseconds));
}

export function backoff(failures: number): number {
  return Math.min(30_000, 1_000 * 2 ** Math.max(0, failures - 1));
}

function abortError(): DOMException {
  return new DOMException("The upload was stopped.", "AbortError");
}

function throwIfAborted(signal: AbortSignal): void {
  if (signal.aborted) {
    throw abortError();
  }
}

function waitFor(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) {
      reject(abortError());
      return;
    }

    const onAbort = () => {
      clearTimeout(timer);
      reject(abortError());
    };

    const timer = setTimeout(() => {
      signal.removeEventListener("abort", onAbort);
      resolve();
    }, milliseconds);

    signal.addEventListener("abort", onAbort, { once: true });
  });
}
