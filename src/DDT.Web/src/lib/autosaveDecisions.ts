// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { ApiError, type ApiProblem } from "./api";
import type { AutosaveState, Revised } from "./autosave";

// What a failed save leads to. stopped and refused are the states of the same names.
export type SaveFailure =
  | { kind: "conflict" }
  | { kind: "stopped"; message: string }
  | { kind: "refused"; message: string; problem: ApiProblem | null }
  | { kind: "retry"; message: string };

// What becomes of a copy read from the server: theirs keeps it beside a conflict, adopt shows it in place of the
// page's copy, and conflict stops saving until the page chooses.
export type Reconciliation = "ignore" | "theirs" | "adopt" | "conflict";

// The autosaver's view of the server's copy when another copy arrives.
export interface KnownCopy<T> {
  revision: number;
  // The server's form of the copy the page last saved or read.
  echo: T;
  state: AutosaveState["kind"];
  dirty: boolean;
}

// Answers that say "not now" rather than "no".
function isTransient(status: number): boolean {
  return status === 408 || status === 429 || status >= 500;
}

function stoppedMessage(status: number, fallback: string): string {
  switch (status) {
    case 401:
      return t`You are signed out, so nothing more is saved. Sign in again and reload the page.`;
    case 403:
      return t`Your account may not change this, so nothing more is saved.`;
    case 404:
      return t`It no longer exists on the server, so nothing more is saved.`;
    default:
      return fallback;
  }
}

// conflicts: whether a 409 says that someone else saved a newer revision, rather than refusing this value.
export function classifyFailure(error: unknown, conflicts: boolean): SaveFailure {
  if (error instanceof ApiError && error.status === 409 && conflicts) {
    return { kind: "conflict" };
  }

  if (error instanceof ApiError && [401, 403, 404].includes(error.status)) {
    return { kind: "stopped", message: stoppedMessage(error.status, error.message) };
  }

  if (error instanceof ApiError && !isTransient(error.status)) {
    return { kind: "refused", message: error.message, problem: error.problem };
  }

  return {
    kind: "retry",
    message: error instanceof ApiError ? error.message : t`The server did not answer.`,
  };
}

// An older copy, or the echo of the page's own save, changes nothing.
export function reconcile<T>(
  copy: Revised<T>,
  known: KnownCopy<T>,
  options: { conflicts: boolean; equals: (a: T, b: T) => boolean },
): Reconciliation {
  const newer = copy.revision > known.revision;

  if (copy.revision < known.revision || (!newer && options.equals(copy.value, known.echo))) {
    return "ignore";
  }

  if (known.state === "conflict") {
    return newer ? "theirs" : "ignore";
  }

  if (known.state === "stopped") {
    return "ignore";
  }

  if (!known.dirty) {
    return "adopt";
  }

  // Without revisions the next save replaces their copy, as the server lets the last save win.
  return newer && options.conflicts ? "conflict" : "ignore";
}

// The wait before the next save: until a pause in typing, but no later than maxWait after the first unsaved edit.
export function dueIn(
  firstEditAt: number | null,
  lastEditAt: number,
  now: number,
  debounce: number,
  maxWait: number,
): number {
  return Math.max(0, Math.min(lastEditAt + debounce, (firstEditAt ?? now) + maxWait) - now);
}

// What a flush waiting for everything to be saved learns: true once nothing is unsaved, false once saving failed or
// cannot go on, and null while a save is due or on its way.
export function flushOutcome(dirty: boolean, state: AutosaveState["kind"]): boolean | null {
  if (!dirty) {
    return true;
  }

  return state === "pending" || state === "saving" ? null : false;
}
