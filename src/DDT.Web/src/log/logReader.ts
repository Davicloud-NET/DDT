// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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

export function createLogReader(
  machineId: string,
  deploymentId: string | null,
  read: Read = readLog,
): LogReader {
  const listeners = new Set<() => void>();
  let snapshot = initial;
  // Every start begins a generation, and reads of an earlier one change nothing.
  let generation = 0;
  let open = false;
  let catchingUp = false;
  let again = false;
  // Pushes set it while a read awaits, which the compiler cannot see in a plain condition.
  const pushedMeanwhile = () => again;

  const update = (from: number, change: Partial<LogSnapshot>) => {
    if (!open || from !== generation) {
      return;
    }

    snapshot = { ...snapshot, ...change };

    for (const listener of [...listeners]) {
      listener();
    }
  };

  const catchUp = async () => {
    if (!open) {
      return;
    }

    // Pushed before the first read answered, or while another read catches up: read again after it.
    if (!snapshot.loaded || catchingUp) {
      again = true;
      return;
    }

    const from = generation;
    catchingUp = true;

    try {
      do {
        again = false;
        let page: MachineLogPage;

        do {
          const after = newestId(snapshot.buffer);
          page = await read(machineId, {
            ...(after === null ? {} : { after }),
            limit: LOG_PAGE_LINES,
            deploymentId,
          });

          if (from !== generation) {
            return;
          }

          update(from, {
            buffer:
              after === null
                ? { lines: page.lines, hasOlder: page.hasOlder }
                : mergeLines(snapshot.buffer, page.lines),
            error: null,
          });
        } while (page.lines.length === LOG_PAGE_LINES);
      } while (pushedMeanwhile());
    } catch (error) {
      update(from, { error: messageOf(error) });
    } finally {
      if (from === generation) {
        catchingUp = false;
      }
    }
  };

  const start = async () => {
    generation += 1;
    open = true;
    catchingUp = false;
    again = false;
    snapshot = initial;

    const from = generation;

    try {
      const page = await read(machineId, { limit: LOG_PAGE_LINES, deploymentId });
      update(from, {
        buffer: mergeLines({ lines: [], hasOlder: page.hasOlder }, page.lines),
        loaded: true,
        error: null,
      });
    } catch (error) {
      // Loaded, so a push or a poll tries again with a read of the newest lines.
      update(from, { loaded: true, error: messageOf(error) });
    }

    if (from === generation && pushedMeanwhile()) {
      await catchUp();
    }
  };

  const loadOlder = async () => {
    const before = oldestId(snapshot.buffer);
    const room = MAX_BUFFERED_LINES - snapshot.buffer.lines.length;

    if (
      !open ||
      before === null ||
      !snapshot.buffer.hasOlder ||
      snapshot.loadingOlder ||
      room <= 0
    ) {
      return;
    }

    const from = generation;
    update(from, { loadingOlder: true });

    try {
      const page = await read(machineId, {
        before,
        limit: Math.min(LOG_PAGE_LINES, room),
        deploymentId,
      });
      update(from, { buffer: withOlder(snapshot.buffer, page), loadingOlder: false, error: null });
    } catch (error) {
      update(from, { loadingOlder: false, error: messageOf(error) });
    }
  };

  return {
    snapshot: () => snapshot,
    subscribe: (listener) => {
      listeners.add(listener);

      return () => {
        listeners.delete(listener);
      };
    },
    start: () => {
      void start();
    },
    catchUp: () => {
      void catchUp();
    },
    loadOlder: () => {
      void loadOlder();
    },
    close: () => {
      open = false;
    },
  };
}
