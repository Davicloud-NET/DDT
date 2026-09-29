// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// The listeners of a store, in the shape useSyncExternalStore subscribes with.
export interface Listeners {
  // Returns the unsubscribe.
  subscribe: (listener: () => void) => () => void;
  notify: () => void;
}

export function createListeners(): Listeners {
  const listeners = new Set<() => void>();

  return {
    subscribe: (listener) => {
      listeners.add(listener);

      return () => {
        listeners.delete(listener);
      };
    },
    // Calls a copy, so a listener that unsubscribes while it is called skips no other.
    notify: () => {
      for (const listener of [...listeners]) {
        listener();
      }
    },
  };
}
