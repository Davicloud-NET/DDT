// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MachineLogEntry, MachineLogPage } from "./log";

// The loaded lines, sorted by id with no duplicates. hasOlder is true when the server holds lines before the
// first one.
export interface LogBuffer {
  lines: readonly MachineLogEntry[];
  hasOlder: boolean;
}

// How many lines of one log a browser tab keeps. Past that the oldest lines are dropped, and a later read
// can bring them back.
export const MAX_BUFFERED_LINES = 20_000;

export const emptyLog: LogBuffer = { lines: [], hasOlder: false };

export function oldestId(buffer: LogBuffer): number | null {
  return buffer.lines[0]?.id ?? null;
}

export function newestId(buffer: LogBuffer): number | null {
  return buffer.lines.at(-1)?.id ?? null;
}

function ascendingOnce(lines: readonly MachineLogEntry[]): MachineLogEntry[] {
  const sorted = [...lines].sort((a, b) => a.id - b.id);

  return sorted.filter((line, index) => index === 0 || sorted[index - 1]?.id !== line.id);
}

// Lines usually arrive after the newest line held, or all before the oldest, so those are just joined on.
// Anything else, such as overlapping reads, is merged by id.
export function mergeLines(
  buffer: LogBuffer,
  incoming: readonly MachineLogEntry[],
  cap = MAX_BUFFERED_LINES,
): LogBuffer {
  if (incoming.length === 0) {
    return buffer;
  }

  const added = ascendingOnce(incoming);
  const oldest = oldestId(buffer);
  const newest = newestId(buffer);
  const firstAdded = added[0]?.id ?? 0;
  const lastAdded = added.at(-1)?.id ?? 0;
  let lines: MachineLogEntry[];

  if (newest === null || firstAdded > newest) {
    lines = [...buffer.lines, ...added];
  } else if (oldest !== null && lastAdded < oldest) {
    lines = [...added, ...buffer.lines];
  } else {
    const byId = new Map(buffer.lines.map((line) => [line.id, line]));

    for (const line of added) {
      byId.set(line.id, line);
    }

    lines = [...byId.values()].sort((a, b) => a.id - b.id);
  }

  return lines.length > cap
    ? { lines: lines.slice(lines.length - cap), hasOlder: true }
    : { lines, hasOlder: buffer.hasOlder };
}

// A page read before the oldest line also says whether even older lines exist.
export function withOlder(buffer: LogBuffer, page: MachineLogPage): LogBuffer {
  return mergeLines({ ...buffer, hasOlder: page.hasOlder }, page.lines);
}
