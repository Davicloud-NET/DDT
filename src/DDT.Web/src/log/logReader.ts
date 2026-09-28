// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { createListeners } from "@/lib/listeners";

import { LOG_PAGE_LINES, readLog, type LogRead, type MachineLogPage } from "./log";
import {
  emptyLog,
  MAX_BUFFERED_LINES,
  mergeLines,
  newestId,
  oldestId,
  withOlder,
  type LogBuffer,
} from "./logBuffer";

export interface LogSnapshot {
  buffer: LogBuffer;
  // True once the first read has answered.
  loaded: boolean;
  loadingOlder: boolean;
  // Why the last read failed. The next successful read clears it.
  error: string | null;
}

// Reads a machine's log, or one run's log, into a buffer. It reads the newest lines first, then new lines as they
// arrive, and older lines on request. Catch-up reads never overlap, and a push during one starts another read
// after it.
export interface LogReader {
  snapshot: () => LogSnapshot;
  subscribe: (listener: () => void) => () => void;
  start: () => void;
  catchUp: () => void;
  loadOlder: () => void;
  // Answers that arrive after close are dropped. start can open the reader again, because React's strict mode
  // closes and restarts it.
  close: () => void;
}

type Read = (machineId: string, read: LogRead) => Promise<MachineLogPage>;

const initial: LogSnapshot = { buffer: emptyLog, loaded: false, loadingOlder: false, error: null };

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

// The reader's snapshot and its generation. Every start begins a new generation. Changes from reads of an earlier
// generation, or after close, are dropped.
function createLogStore() {
  const listeners = createListeners();
  let snapshot = initial;
  let generation = 0;
  let open = false;

  return {
    snapshot: () => snapshot,
    subscribe: listeners.subscribe,
    isOpen: () => open,
    generation: () => generation,
    isCurrent: (from: number) => from === generation,
    begin: () => {
      generation += 1;
      open = true;
      snapshot = initial;

      return generation;
    },
    update: (from: number, change: Partial<LogSnapshot>) => {
      if (!open || from !== generation) {
        return;
      }

      snapshot = { ...snapshot, ...change };
      listeners.notify();
    },
    close: () => {
      open = false;
    },
  };
}

type LogStore = ReturnType<typeof createLogStore>;

// Reads the pages after the newest line until a page comes back short. Returns false if a later start made the
// read stale.
async function readNewer(
  store: LogStore,
  from: number,
  readAfter: (after: number | null) => Promise<MachineLogPage>,
): Promise<boolean> {
  let page: MachineLogPage;

  do {
    const after = newestId(store.snapshot().buffer);
    page = await readAfter(after);

    if (!store.isCurrent(from)) {
      return false;
    }

    store.update(from, {
      buffer:
        after === null
          ? { lines: page.lines, hasOlder: page.hasOlder }
          : mergeLines(store.snapshot().buffer, page.lines),
      error: null,
    });
  } while (page.lines.length === LOG_PAGE_LINES);

  return true;
}

// The read for the lines before the oldest one, or null when there are none or no more fit in the buffer.
function olderRead(snapshot: LogSnapshot): { before: number; limit: number } | null {
  const before = oldestId(snapshot.buffer);
  const room = MAX_BUFFERED_LINES - snapshot.buffer.lines.length;

  return before === null || !snapshot.buffer.hasOlder || snapshot.loadingOlder || room <= 0
    ? null
    : { before, limit: Math.min(LOG_PAGE_LINES, room) };
}

export function createLogReader(
  machineId: string,
  deploymentId: string | null,
  read: Read = readLog,
): LogReader {
  const store = createLogStore();
  let catchingUp = false;
  let again = false;
  // Pushes set again while a read awaits. The compiler can't see that in a plain condition, hence the function.
  const pushedMeanwhile = () => again;

  const readAfter = (after: number | null) =>
    read(machineId, { ...(after === null ? {} : { after }), limit: LOG_PAGE_LINES, deploymentId });

  const catchUp = async () => {
    if (!store.isOpen()) {
      return;
    }

    // A push before the first read answered, or during another catch-up, reads again after that read.
    if (!store.snapshot().loaded || catchingUp) {
      again = true;
      return;
    }

    const from = store.generation();
    catchingUp = true;

    try {
      do {
        again = false;

        if (!(await readNewer(store, from, readAfter))) {
          return;
        }
      } while (pushedMeanwhile());
    } catch (error) {
      store.update(from, { error: messageOf(error) });
    } finally {
      if (store.isCurrent(from)) {
        catchingUp = false;
      }
    }
  };

  const start = async () => {
    const from = store.begin();
    catchingUp = false;
    again = false;

    try {
      const page = await read(machineId, { limit: LOG_PAGE_LINES, deploymentId });
      store.update(from, {
        buffer: mergeLines({ lines: [], hasOlder: page.hasOlder }, page.lines),
        loaded: true,
        error: null,
      });
    } catch (error) {
      // Marked as loaded anyway, so the next push or poll tries again with a read of the newest lines.
      store.update(from, { loaded: true, error: messageOf(error) });
    }

    if (store.isCurrent(from) && pushedMeanwhile()) {
      await catchUp();
    }
  };

  const loadOlder = async () => {
    const older = olderRead(store.snapshot());

    if (!store.isOpen() || older === null) {
      return;
    }

    const from = store.generation();
    store.update(from, { loadingOlder: true });

    try {
      const page = await read(machineId, { ...older, deploymentId });
      store.update(from, {
        buffer: withOlder(store.snapshot().buffer, page),
        loadingOlder: false,
        error: null,
      });
    } catch (error) {
      store.update(from, { loadingOlder: false, error: messageOf(error) });
    }
  };

  return {
    snapshot: store.snapshot,
    subscribe: store.subscribe,
    start: () => {
      void start();
    },
    catchUp: () => {
      void catchUp();
    },
    loadOlder: () => {
      void loadOlder();
    },
    close: store.close,
  };
}
