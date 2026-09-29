// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Callers waiting for one outcome, all told at once.
export interface Waiters<T> {
  add: (waiter: (outcome: T) => void) => void;
  count: () => number;
  // A waiter added while the others are told waits for the next outcome.
  settle: (outcome: T) => void;
}

export function createWaiters<T>(): Waiters<T> {
  let waiters: ((outcome: T) => void)[] = [];

  return {
    add: (waiter) => {
      waiters.push(waiter);
    },
    count: () => waiters.length,
    settle: (outcome) => {
      const done = waiters;
      waiters = [];

      for (const waiter of done) {
        waiter(outcome);
      }
    },
  };
}
