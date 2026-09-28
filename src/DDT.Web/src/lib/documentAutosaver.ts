// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type {
  AutosaveOptions,
  AutosaveSnapshot,
  AutosaveState,
  Autosaver,
  Revised,
} from "./autosave";
import { classifyFailure, dueIn, flushOutcome, reconcile } from "./autosaveDecisions";
import { backoff } from "./backoff";
import { createListeners } from "./listeners";
import { createWaiters } from "./waiters";

export type ResolvedAutosaveOptions<T, R> = AutosaveOptions<T, R> & {
  debounceMs: number;
  maxWaitMs: number;
};

// The autosaver createAutosaver hands out. Its operations are arrow functions, so a page may pass them on unbound.
export class DocumentAutosaver<T, R> implements Autosaver<T> {
  readonly #options: ResolvedAutosaveOptions<T, R>;
  readonly #listeners = createListeners();
  readonly #waiters = createWaiters<boolean>();
  #value: T;
  // What the server holds as this page knows it: the copy it last sent or read.
  #server: T;
  #revision: number;
  // The server's own form of that copy, for example with the name trimmed. A read returns it again. Taking it
  // over while someone types would move the text under the cursor.
  #echo: T;
  #state: AutosaveState = { kind: "saved", at: null };
  #theirs: Revised<T> | null = null;
  #savedAt: number | null = null;
  #inFlight = false;
  // A copy read while a save was on its way. It's checked once the save has answered.
  #received: Revised<T> | null = null;
  #firstEditAt: number | null = null;
  #lastEditAt = 0;
  #failures = 0;
  #timer: ReturnType<typeof setTimeout> | undefined;
  #open = true;
  #snapshot: AutosaveSnapshot<T>;

  public constructor(options: ResolvedAutosaveOptions<T, R>) {
    this.#options = options;
    this.#value = options.initial.value;
    this.#server = options.initial.value;
    this.#revision = options.initial.revision;
    this.#echo = options.initial.value;
    this.#snapshot = this.#build();
  }

  public snapshot = (): AutosaveSnapshot<T> => this.#snapshot;

  public subscribe = (listener: () => void): (() => void) => this.#listeners.subscribe(listener);

  public update = (change: (current: T) => T, immediate = false): void => {
    this.#value = change(this.#value);

    if (!this.#isDirty()) {
      if (this.#state.kind === "pending" || this.#state.kind === "refused") {
        clearTimeout(this.#timer);
        this.#firstEditAt = null;
        this.#state = { kind: "saved", at: this.#savedAt };
      }

      this.#publish();
      return;
    }

    const now = Date.now();
    this.#firstEditAt ??= now;
    this.#lastEditAt = now;

    // A save on its way, a retry and a conflict each send the latest value when they are done.
    if (
      !this.#inFlight &&
      (this.#state.kind === "saved" ||
        this.#state.kind === "pending" ||
        this.#state.kind === "refused")
    ) {
      this.#state = { kind: "pending" };
      this.#schedule(immediate);
    }

    this.#publish();
  };

  public receive = (value: T, revision: number): void => {
    const copy = { value, revision };

    if (this.#inFlight) {
      if (this.#received === null || copy.revision >= this.#received.revision) {
        this.#received = copy;
      }

      return;
    }

    this.#accept(copy);
    this.#publish();
  };

  public flush = (keepalive = false): Promise<boolean> => {
    const sendable = !this.#inFlight && this.#isDirty() && this.#isDue();

    if (keepalive) {
      if (sendable) {
        void this.#send(true);
      }

      return Promise.resolve(!this.#isDirty());
    }

    return new Promise((resolve) => {
      this.#waiters.add(resolve);

      if (sendable) {
        void this.#send(false);
      } else {
        this.#settleWaiters();
      }
    });
  };

  public takeTheirs = (): void => {
    if (this.#state.kind === "conflict" && this.#theirs !== null) {
      this.#adopt(this.#theirs);
      this.#publish();
    }
  };

  public keepMine = (): void => {
    if (this.#state.kind !== "conflict" || this.#theirs === null) {
      return;
    }

    // Their copy becomes the base, so the next save goes over their revision.
    this.#server = this.#theirs.value;
    this.#echo = this.#theirs.value;
    this.#revision = this.#theirs.revision;
    this.#theirs = null;
    this.#state = { kind: "pending" };
    void this.#send(false);
    this.#publish();
  };

  public stop = (message: string): void => {
    clearTimeout(this.#timer);
    this.#state = { kind: "stopped", message };
    this.#publish();
  };

  public start = (): void => {
    this.#open = true;
  };

  public close = (): void => {
    this.#open = false;
    clearTimeout(this.#timer);

    if (!this.#inFlight && this.#isDirty() && this.#isDue()) {
      void this.#send(false);
    }
  };

  #isDirty(): boolean {
    return !this.#options.equals(this.#value, this.#server);
  }

  #isBlocked(): boolean {
    return this.#state.kind === "conflict" || this.#state.kind === "stopped";
  }

  // A save waits for a pause in typing or for its retry.
  #isDue(): boolean {
    return this.#state.kind === "pending" || this.#state.kind === "retrying";
  }

  #build(): AutosaveSnapshot<T> {
    return {
      value: this.#value,
      base: this.#server,
      state: this.#state,
      dirty: this.#isDirty(),
      theirs: this.#theirs,
    };
  }

  #publish(): void {
    this.#snapshot = this.#build();
    this.#listeners.notify();
    this.#settleWaiters();
  }

  #settleWaiters(): void {
    if (this.#waiters.count() === 0 || this.#inFlight) {
      return;
    }

    const saved = flushOutcome(this.#isDirty(), this.#state.kind);

    if (saved !== null) {
      this.#waiters.settle(saved);
    }
  }

  #schedule(immediate: boolean): void {
    clearTimeout(this.#timer);

    const { debounceMs, maxWaitMs } = this.#options;
    const delay = immediate
      ? 0
      : dueIn(this.#firstEditAt, this.#lastEditAt, Date.now(), debounceMs, maxWaitMs);

    this.#timer = setTimeout(() => {
      void this.#send(false);
    }, delay);
  }

  #adopt(copy: Revised<T>): void {
    clearTimeout(this.#timer);
    this.#value = copy.value;
    this.#server = copy.value;
    this.#echo = copy.value;
    this.#revision = copy.revision;
    this.#theirs = null;
    this.#firstEditAt = null;
    this.#failures = 0;
    this.#state = { kind: "saved", at: this.#savedAt };
    this.#options.onTakenIn?.(copy);
  }

  #accept(copy: Revised<T>): void {
    const known = {
      revision: this.#revision,
      echo: this.#echo,
      state: this.#state.kind,
      dirty: this.#isDirty(),
    };
    const { equals } = this.#options;

    switch (reconcile(copy, known, { conflicts: this.#options.conflicts === true, equals })) {
      case "theirs":
        this.#theirs = copy;
        break;
      case "adopt":
        this.#adopt(copy);
        break;
      case "conflict":
        clearTimeout(this.#timer);
        this.#theirs = copy;
        this.#state = { kind: "conflict" };
        break;
      case "ignore":
        break;
    }
  }

  async #send(keepalive: boolean): Promise<void> {
    if (this.#inFlight || this.#isBlocked()) {
      return;
    }

    clearTimeout(this.#timer);

    if (!this.#isDirty()) {
      if (this.#state.kind !== "saved") {
        this.#state = { kind: "saved", at: this.#savedAt };
        this.#publish();
      }

      return;
    }

    const sent = this.#value;
    this.#inFlight = true;
    this.#firstEditAt = null;
    this.#state = { kind: "saving" };
    this.#publish();

    let result: R;

    try {
      result = await this.#options.save(sent, this.#revision, keepalive);
    } catch (error) {
      this.#inFlight = false;
      this.#failed(error);
      return;
    }

    this.#inFlight = false;
    this.#succeeded(result, sent);
  }

  #succeeded(result: R, sent: T): void {
    const copy = this.#options.savedAs(result);

    this.#server = sent;
    this.#echo = copy.value;
    this.#revision = copy.revision;
    this.#failures = 0;
    this.#savedAt = Date.now();
    this.#options.onSaved?.(result);

    const later = this.#takeReceived();

    if (later !== null) {
      this.#accept(later);
    }

    if (!this.#isBlocked()) {
      this.#goOn();
    }

    this.#publish();
  }

  // After a save, what was typed meanwhile is saved next. If a page waits for everything to be saved, or is gone,
  // the next save doesn't wait for a pause.
  #goOn(): void {
    if (!this.#isDirty()) {
      this.#state = { kind: "saved", at: this.#savedAt };
      return;
    }

    this.#state = { kind: "pending" };
    this.#firstEditAt ??= Date.now();

    if (this.#open && this.#waiters.count() === 0) {
      this.#schedule(false);
    } else {
      void this.#send(false);
    }
  }

  #failed(error: unknown): void {
    const later = this.#takeReceived();
    const failure = classifyFailure(error, this.#options.conflicts === true);

    if (failure.kind === "conflict") {
      this.#state = { kind: "conflict" };

      if (later !== null && later.revision > this.#revision) {
        this.#theirs = later;
      }

      this.#publish();
      this.#options.onConflict?.();
      return;
    }

    if (later !== null) {
      this.#accept(later);
    }

    if (!this.#isBlocked()) {
      if (failure.kind === "retry") {
        this.#retry(failure.message);
      } else {
        this.#state = failure;
      }
    }

    this.#publish();
  }

  #retry(message: string): void {
    this.#failures++;

    const delay = backoff(this.#failures);

    this.#state = {
      kind: "retrying",
      attempt: this.#failures,
      nextAt: Date.now() + delay,
      message,
    };

    if (this.#open) {
      this.#timer = setTimeout(() => {
        void this.#send(false);
      }, delay);
    }
  }

  #takeReceived(): Revised<T> | null {
    const later = this.#received;
    this.#received = null;

    return later;
  }
}
