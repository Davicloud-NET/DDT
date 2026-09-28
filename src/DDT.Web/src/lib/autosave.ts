// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ApiProblem } from "./api";
import { backoff } from "./backoff";
import { DocumentAutosaver } from "./documentAutosaver";

// refused: the server refused this value, and the next edit tries again. conflict: someone saved first, and nothing
// is saved until the page chooses a copy. stopped: nothing more can be saved, such as for a deleted document.
export type AutosaveState =
  | { kind: "saved"; at: number | null }
  | { kind: "pending" }
  | { kind: "saving" }
  | { kind: "retrying"; attempt: number; nextAt: number; message: string }
  | { kind: "refused"; message: string; problem: ApiProblem | null }
  | { kind: "conflict" }
  | { kind: "stopped"; message: string };

// A copy of the document and the revision it has on the server. Documents the server saves without revisions
// use 0 throughout, and the last save wins.
export interface Revised<T> {
  value: T;
  revision: number;
}

export interface AutosaveSnapshot<T> {
  // What this page shows and edits.
  value: T;
  // The server's copy as this page last saved or read it.
  base: T;
  state: AutosaveState;
  dirty: boolean;
  // The newer copy someone else saved, while in conflict. Null until it is read.
  theirs: Revised<T> | null;
}

export interface AutosaveOptions<T, R> {
  initial: Revised<T>;
  save: (value: T, revision: number, keepalive: boolean) => Promise<R>;
  // The server's form of the copy a save stored. Later reads of the document return the same form.
  savedAs: (result: R) => Revised<T>;
  equals: (a: T, b: T) => boolean;
  // Whether a 409 means someone else saved a newer revision. Otherwise a 409 refuses this value.
  conflicts?: boolean;
  onSaved?: (result: R) => void;
  // Called when a save finds a newer revision, so the page reads it.
  onConflict?: () => void;
  // Called when the page shows someone else's copy in place of its own. That's a copy read while nothing was
  // unsaved, or theirs taken after a conflict.
  onTakenIn?: (copy: Revised<T>) => void;
  debounceMs?: number;
  maxWaitMs?: number;
}

export interface Autosaver<T> {
  snapshot: () => AutosaveSnapshot<T>;
  subscribe: (listener: () => void) => () => void;
  // Structural edits, such as adding a step, save at once; typing waits for a pause.
  update: (change: (value: T) => T, immediate?: boolean) => void;
  // A copy read from the server, for example after another page saved.
  receive: (value: T, revision: number) => void;
  // Resolves true once everything is saved, and false if saving failed or can't continue. keepalive sends what's
  // there without waiting, for a page that's being closed.
  flush: (keepalive?: boolean) => Promise<boolean>;
  takeTheirs: () => void;
  keepMine: () => void;
  stop: (message: string) => void;
  start: () => void;
  // Sends what is not saved yet, without retries, and schedules nothing more.
  close: () => void;
}

export const DEBOUNCE_MS = 700;
export const MAX_WAIT_MS = 3_000;

// The wait before an autosave retries.
export const retryDelay = backoff;

// Saves a document while it is edited, one save at a time with the last revision. It lives outside React, so a save
// that finishes after the page is gone still records what the server holds.
export function createAutosaver<T, R>(options: AutosaveOptions<T, R>): Autosaver<T> {
  return new DocumentAutosaver({
    ...options,
    debounceMs: options.debounceMs ?? DEBOUNCE_MS,
    maxWaitMs: options.maxWaitMs ?? MAX_WAIT_MS,
  });
}
