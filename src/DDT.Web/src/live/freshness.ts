// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { LiveStatus } from "./liveConnection";

// How often a page reads again what the hub would push, while the live connection is down.
export const POLL_MS = 5_000;

// For a list the hub keeps current. While changes arrive by push, it's never read again on mount or focus,
// because a reconnect reads it once more. While they don't, it's read every POLL_MS.
export function liveListOptions(status: LiveStatus) {
  return status === "live"
    ? { staleTime: Infinity, refetchOnWindowFocus: false, refetchInterval: false as const }
    : { staleTime: 0, refetchOnWindowFocus: true, refetchInterval: POLL_MS };
}
