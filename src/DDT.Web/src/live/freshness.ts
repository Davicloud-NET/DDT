// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { LiveStatus } from "./liveConnection";

// How often a page reads again what the hub would push, while the live connection is down.
export const POLL_MS = 5_000;

// For a list the hub keeps current. While changes arrive by push, a list read once stays as it is: opening a page,
// a panel or the window again does not read it again, and the hub reads it once more after a reconnect. While they
// do not arrive, the list is read every few seconds.
export function liveListOptions(status: LiveStatus) {
  return status === "live"
    ? { staleTime: Infinity, refetchOnWindowFocus: false, refetchInterval: false as const }
    : { staleTime: 0, refetchOnWindowFocus: true, refetchInterval: POLL_MS };
}
