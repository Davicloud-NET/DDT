// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import { UNSTABLE_ToastQueue as ToastQueue } from "react-aria-components";

// Short-lived news about something the person did not do on this page, such as a machine finishing its run.
// Anything that needs an answer is a dialog or a notice instead. Failures stay until they are closed.
export interface ToastMessage {
  title: ReactNode;
  description?: ReactNode;
  tone?: "ok" | "fail" | "info";
  // A key that answers the news, such as Undo after a removal; pressing it closes the toast.
  action?: { label: ReactNode; onAction: () => void };
}

// React Aria removes a closed toast at once. This queue keeps a shown one on screen while it leaves: closing it, by
// its key or its timeout, only marks it as leaving, and the toast finishes the close once its exit has run.
export class LeavingToastQueue<T> extends ToastQueue<T> {
  #leaving: ReadonlySet<string> = new Set();
  readonly #listeners = new Set<() => void>();

  override close(key: string): void {
    if (!this.visibleToasts.some((toast) => toast.key === key)) {
      super.close(key);
      return;
    }

    if (!this.#leaving.has(key)) {
      this.#leaving = new Set([...this.#leaving, key]);
      this.#emit();
    }
  }

  // Removes a toast that has left.
  finishClose(key: string): void {
    if (!this.#leaving.has(key)) {
      return;
    }

    this.#leaving = new Set([...this.#leaving].filter((leaving) => leaving !== key));
    super.close(key);
    this.#emit();
  }

  // Removes every toast at once, leaving or not.
  override clear(): void {
    this.#leaving = new Set();
    super.clear();
    this.#emit();
  }

  isLeaving = (key: string): boolean => this.#leaving.has(key);

  subscribeLeaving = (listener: () => void): (() => void) => {
    this.#listeners.add(listener);

    return () => {
      this.#listeners.delete(listener);
    };
  };

  #emit(): void {
    for (const listener of [...this.#listeners]) {
      listener();
    }
  }
}

export const toasts = new LeavingToastQueue<ToastMessage>({ maxVisibleToasts: 4 });

// Returns the toast's key, to close it early.
export function showToast(message: ToastMessage, timeout = 6000): string {
  return toasts.add(message, message.tone === "fail" ? {} : { timeout });
}
