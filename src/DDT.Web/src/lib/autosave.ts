// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { ApiError, type ApiProblem } from "./api";

// "refused": the server refused this value, for example a name another sequence has; the next edit tries
// again. "conflict": someone else saved first, and nothing is saved until the page chooses a copy. "stopped":
// nothing more can be saved, for example because the document was deleted.
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
  // The server's form of the copy a save stored, which later reads of the document repeat.
  savedAs: (result: R) => Revised<T>;
  equals: (a: T, b: T) => boolean;
  // Whether a 409 says that someone else saved a newer revision. Otherwise it refuses this value.
  conflicts?: boolean;
  onSaved?: (result: R) => void;
  // Called when a save finds a newer revision, so the page reads it.
  onConflict?: () => void;
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
  // Resolves true once everything is saved, false when saving failed or cannot go on. keepalive sends what is
  // there and does not wait, for a page that is being closed.
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

// The upload's back off: 1 s, doubling up to 30 s.
export function retryDelay(attempt: number): number {
  return Math.min(30_000, 1_000 * 2 ** Math.max(0, attempt - 1));
}

// Answers that say "not now" rather than "no".
function isTransient(status: number): boolean {
  return status === 408 || status === 429 || status >= 500;
}

function stoppedMessage(status: number, fallback: string): string {
  switch (status) {
    case 401:
      return "You are signed out, so nothing more is saved. Sign in again and reload the page.";
    case 403:
      return "Your account may not change this, so nothing more is saved.";
    case 404:
      return "It no longer exists on the server, so nothing more is saved.";
    default:
      return fallback;
  }
}

// Saves a document while it is edited: after a pause in typing, at least every few seconds while typing goes
// on, one save at a time with the revision the last one returned. It lives outside React, so a save that
// finishes after the page is gone still records what the server holds.
export function createAutosaver<T, R>(options: AutosaveOptions<T, R>): Autosaver<T> {
  const debounceMs = options.debounceMs ?? DEBOUNCE_MS;
  const maxWaitMs = options.maxWaitMs ?? MAX_WAIT_MS;
  const listeners = new Set<() => void>();

  let value = options.initial.value;
  // What the server holds as this page knows it: the copy it last sent or read.
  let server = options.initial.value;
  let revision = options.initial.revision;
  // The server's own form of that copy, for example with the name trimmed. A read repeats it, and taking it
  // over while someone types would move the text under the cursor.
  let echo = options.initial.value;
  let state: AutosaveState = { kind: "saved", at: null };
  let theirs: Revised<T> | null = null;
  let savedAt: number | null = null;
  let inFlight = false;
  // A copy read while a save was on its way, looked at once the save answered.
  let received: Revised<T> | null = null;
  let firstEditAt: number | null = null;
  let lastEditAt = 0;
  let failures = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let open = true;
  let waiters: ((saved: boolean) => void)[] = [];

  const isDirty = () => !options.equals(value, server);
  const isBlocked = () => state.kind === "conflict" || state.kind === "stopped";

  const build = (): AutosaveSnapshot<T> => ({
    value,
    base: server,
    state,
    dirty: isDirty(),
    theirs,
  });

  let snapshot = build();

  const settleWaiters = () => {
    if (waiters.length === 0 || inFlight) {
      return;
    }

    let saved: boolean;

    if (!isDirty()) {
      saved = true;
    } else if (state.kind === "pending" || state.kind === "saving") {
      return;
    } else {
      saved = false;
    }

    const done = waiters;
    waiters = [];

    for (const waiter of done) {
      waiter(saved);
    }
  };

  const publish = () => {
    snapshot = build();

    for (const listener of [...listeners]) {
      listener();
    }

    settleWaiters();
  };

  const schedule = (immediate: boolean) => {
    clearTimeout(timer);

    const now = Date.now();
    const due = Math.min(lastEditAt + debounceMs, (firstEditAt ?? now) + maxWaitMs);

    timer = setTimeout(
      () => {
        void send(false);
      },
      immediate ? 0 : Math.max(0, due - now),
    );
  };

  const adopt = (copy: Revised<T>) => {
    clearTimeout(timer);
    value = copy.value;
    server = copy.value;
    echo = copy.value;
    revision = copy.revision;
    theirs = null;
    firstEditAt = null;
    failures = 0;
    state = { kind: "saved", at: savedAt };
  };

  const accept = (copy: Revised<T>) => {
    const newer = copy.revision > revision;

    if (copy.revision < revision || (!newer && options.equals(copy.value, echo))) {
      return;
    }

    if (state.kind === "conflict") {
      if (newer) {
        theirs = copy;
      }

      return;
    }

    if (state.kind === "stopped") {
      return;
    }

    if (!isDirty()) {
      adopt(copy);
      return;
    }

    // Without revisions the next save replaces their copy, as the server lets the last save win.
    if (newer && options.conflicts === true) {
      clearTimeout(timer);
      theirs = copy;
      state = { kind: "conflict" };
    }
  };

  const succeeded = (result: R, sent: T) => {
    const copy = options.savedAs(result);

    server = sent;
    echo = copy.value;
    revision = copy.revision;
    failures = 0;
    savedAt = Date.now();
    options.onSaved?.(result);

    const later = received;
    received = null;

    if (later !== null) {
      accept(later);
    }

    if (!isBlocked()) {
      if (isDirty()) {
        state = { kind: "pending" };
        firstEditAt ??= Date.now();

        // A page that waits for everything to be saved, or is gone, does not wait for a pause.
        if (open && waiters.length === 0) {
          schedule(false);
        } else {
          void send(false);
        }
      } else {
        state = { kind: "saved", at: savedAt };
      }
    }

    publish();
  };

  const failed = (error: unknown) => {
    const later = received;
    received = null;

    if (error instanceof ApiError && error.status === 409 && options.conflicts === true) {
      state = { kind: "conflict" };

      if (later !== null && later.revision > revision) {
        theirs = later;
      }

      publish();
      options.onConflict?.();
      return;
    }

    if (later !== null) {
      accept(later);
    }

    if (isBlocked()) {
      publish();
      return;
    }

    if (error instanceof ApiError && [401, 403, 404].includes(error.status)) {
      state = { kind: "stopped", message: stoppedMessage(error.status, error.message) };
    } else if (error instanceof ApiError && !isTransient(error.status)) {
      state = { kind: "refused", message: error.message, problem: error.problem };
    } else {
      failures++;

      const delay = retryDelay(failures);

      state = {
        kind: "retrying",
        attempt: failures,
        nextAt: Date.now() + delay,
        message: error instanceof ApiError ? error.message : "The server did not answer.",
      };

      if (open) {
        timer = setTimeout(() => {
          void send(false);
        }, delay);
      }
    }

    publish();
  };

  async function send(keepalive: boolean): Promise<void> {
    if (inFlight || isBlocked()) {
      return;
    }

    clearTimeout(timer);

    if (!isDirty()) {
      if (state.kind !== "saved") {
        state = { kind: "saved", at: savedAt };
        publish();
      }

      return;
    }

    const sent = value;
    inFlight = true;
    firstEditAt = null;
    state = { kind: "saving" };
    publish();

    let result: R;

    try {
      result = await options.save(sent, revision, keepalive);
    } catch (error) {
      inFlight = false;
      failed(error);
      return;
    }

    inFlight = false;
    succeeded(result, sent);
  }

  const update = (change: (current: T) => T, immediate = false) => {
    value = change(value);

    if (!isDirty()) {
      if (state.kind === "pending" || state.kind === "refused") {
        clearTimeout(timer);
        firstEditAt = null;
        state = { kind: "saved", at: savedAt };
      }

      publish();
      return;
    }

    const now = Date.now();
    firstEditAt ??= now;
    lastEditAt = now;

    // A save on its way, a retry and a conflict each send the latest value when they are done.
    if (
      !inFlight &&
      (state.kind === "saved" || state.kind === "pending" || state.kind === "refused")
    ) {
      state = { kind: "pending" };
      schedule(immediate);
    }

    publish();
  };

  const receive = (copyValue: T, copyRevision: number) => {
    const copy = { value: copyValue, revision: copyRevision };

    if (inFlight) {
      if (received === null || copy.revision >= received.revision) {
        received = copy;
      }

      return;
    }

    accept(copy);
    publish();
  };

  const flush = (keepalive = false): Promise<boolean> => {
    const sendable =
      !inFlight && isDirty() && (state.kind === "pending" || state.kind === "retrying");

    if (keepalive) {
      if (sendable) {
        void send(true);
      }

      return Promise.resolve(!isDirty());
    }

    return new Promise((resolve) => {
      waiters.push(resolve);

      if (sendable) {
        void send(false);
      } else {
        settleWaiters();
      }
    });
  };

  return {
    snapshot: () => snapshot,
    subscribe: (listener) => {
      listeners.add(listener);

      return () => {
        listeners.delete(listener);
      };
    },
    update,
    receive,
    flush,
    takeTheirs: () => {
      if (state.kind === "conflict" && theirs !== null) {
        adopt(theirs);
        publish();
      }
    },
    keepMine: () => {
      if (state.kind !== "conflict" || theirs === null) {
        return;
      }

      // Their copy becomes the base, so the next save goes over their revision.
      server = theirs.value;
      echo = theirs.value;
      revision = theirs.revision;
      theirs = null;
      state = { kind: "pending" };
      void send(false);
      publish();
    },
    stop: (message) => {
      clearTimeout(timer);
      state = { kind: "stopped", message };
      publish();
    },
    start: () => {
      open = true;
    },
    close: () => {
      open = false;
      clearTimeout(timer);

      if (!inFlight && isDirty() && (state.kind === "pending" || state.kind === "retrying")) {
        void send(false);
      }
    },
  };
}
