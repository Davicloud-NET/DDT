// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { ApiError, apiErrorFrom } from "@/lib/api";

const MAX_WAIT = 60_000;

export async function refusal(response: Response): Promise<ApiError> {
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

export function isTransient(status: number): boolean {
  return transientStatuses.includes(status);
}

export function readOffset(response: Response, length: number): number | null {
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

export function retryAfter(response: Response): number | null {
  const header = response.headers.get("Retry-After");

  if (header === null || header.trim() === "") {
    return null;
  }

  const seconds = Number(header);
  const milliseconds = Number.isFinite(seconds) ? seconds * 1000 : Date.parse(header) - Date.now();

  return Number.isNaN(milliseconds) ? null : Math.min(MAX_WAIT, Math.max(0, milliseconds));
}
