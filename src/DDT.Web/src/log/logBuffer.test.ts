// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { MachineLogEntry } from "./log";
import { emptyLog, mergeLines, newestId, oldestId, withOlder, type LogBuffer } from "./logBuffer";

function line(id: number): MachineLogEntry {
  return {
    id,
    timestampUtc: "2026-09-16T10:00:00Z",
    receivedUtc: "2026-09-16T10:00:00Z",
    level: "Information",
    message: `Line ${String(id)}`,
    agentTimestampUtc: "2026-09-16T10:00:00Z",
    deploymentId: null,
    stepId: null,
  };
}

function ids(buffer: LogBuffer): number[] {
  return buffer.lines.map((entry) => entry.id);
}

describe("the log buffer", () => {
  it("appends newer lines and keeps each id once", () => {
    const first = mergeLines(emptyLog, [line(1), line(2)]);
    const second = mergeLines(first, [line(2), line(3), line(3), line(4)]);

    expect(ids(second)).toEqual([1, 2, 3, 4]);
    expect(oldestId(second)).toBe(1);
    expect(newestId(second)).toBe(4);
  });

  it("puts lines that arrive out of order where they belong", () => {
    const buffer = mergeLines(mergeLines(emptyLog, [line(1), line(5)]), [
      line(4),
      line(2),
      line(6),
    ]);

    expect(ids(buffer)).toEqual([1, 2, 4, 5, 6]);
  });

  it("drops the oldest lines past its cap and says older ones exist", () => {
    const buffer = mergeLines(emptyLog, [line(1), line(2), line(3)], 5);
    const capped = mergeLines(buffer, [line(4), line(5), line(6), line(7)], 5);

    expect(ids(capped)).toEqual([3, 4, 5, 6, 7]);
    expect(capped.hasOlder).toBe(true);
  });

  it("puts older lines first and takes from their page whether still older ones exist", () => {
    const buffer = mergeLines({ lines: [], hasOlder: true }, [line(5), line(6)]);
    const older = withOlder(buffer, { lines: [line(3), line(4)], hasOlder: false });

    expect(ids(older)).toEqual([3, 4, 5, 6]);
    expect(older.hasOlder).toBe(false);
  });
});
