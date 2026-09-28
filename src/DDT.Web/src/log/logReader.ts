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
  // The first read answered.
  loaded: boolean;
  loadingOlder: boolean;
  // Why the last read failed; the next one that succeeds clears it.
  error: string | null;
}

// Reads one machine's log, or one run's, into a buffer: the newest lines first, then what arrives after them,
// and older lines on request. Reads that catch up never overlap, and a push during one reads again after it.
export interface LogReader {
  snapshot: () => LogSnapshot;
  subscribe: (listener: () => void) => () => void;
  start: () => void;
  catchUp: () => void;
  loadOlder: () => void;
  // Answers that arrive later are dropped. start opens the reader again, as React's strict mode does.
  close: () => void;
}

type Read = (machineId: string, read: LogRead) => Promise<MachineLogPage>;

const initial: LogSnapshot = { buffer: emptyLog, loaded: false, loadingOlder: false, error: null };

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

// The reader's snapshot and its generations. Every start begins a generation, and changes from reads of an earlier
// one, or after close, are dropped.
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

// Reads the pages after the newest line until one comes short. False once a later start made the read stale.
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

// The read of the lines before the oldest, or null where there are none or no more fit.
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
  // Pushes set it while a read awaits, which the compiler cannot see in a plain condition.
  const pushedMeanwhile = () => again;

  const readAfter = (after: number | null) =>
    read(machineId, { ...(after === null ? {} : { after }), limit: LOG_PAGE_LINES, deploymentId });

  const catchUp = async () => {
    if (!store.isOpen()) {
      return;
    }

    // Pushed before the first read answered, or while another read catches up: read again after it.
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
      // Loaded, so a push or a poll tries again with a read of the newest lines.
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
