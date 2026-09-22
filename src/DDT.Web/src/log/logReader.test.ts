// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { LOG_PAGE_LINES, type LogRead, type MachineLogEntry, type MachineLogPage } from "./log";
import { MAX_BUFFERED_LINES } from "./logBuffer";
import { createLogReader, type LogReader } from "./logReader";

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

function lines(from: number, to: number): MachineLogEntry[] {
  return Array.from({ length: to - from + 1 }, (_, offset) => line(from + offset));
}

interface Waiting {
  read: LogRead;
  answer: (lines: MachineLogEntry[], hasOlder?: boolean) => Promise<void>;
}

// A server whose reads wait until the test answers them, oldest first.
function slowServer() {
  const waiting: Waiting[] = [];
  const reads: LogRead[] = [];

  const read = (_machineId: string, request: LogRead) =>
    new Promise<MachineLogPage>((resolve) => {
      reads.push(request);
      waiting.push({
        read: request,
        answer: async (answered, hasOlder = false) => {
          resolve({ lines: answered, hasOlder });
          // Lets the reader take the answer and send its next read.
          await new Promise((settled) => setTimeout(settled, 0));
        },
      });
    });

  return {
    read,
    reads,
    waiting: () => waiting.length,
    next: (): Waiting => {
      const first = waiting.shift();

      if (first === undefined) {
        throw new Error("No read is waiting.");
      }

      return first;
    },
  };
}

function ids(reader: LogReader): number[] {
  return reader.snapshot().buffer.lines.map((entry) => entry.id);
}

describe("the log reader", () => {
  it("reads once more, after the newest line, when a push arrives during a read", async () => {
    const server = slowServer();
    const reader = createLogReader("m", null, server.read);

    reader.start();
    await server.next().answer(lines(1, 3));

    reader.catchUp();
    reader.catchUp();
    expect(server.waiting()).toBe(1);

    await server.next().answer([line(4)]);
    expect(server.next().read).toMatchObject({ after: 4 });
    expect(server.waiting()).toBe(0);
    expect(server.reads.map((request) => request.after)).toEqual([undefined, 3, 4]);
  });

  it("reads what was pushed before the first answer once it arrives", async () => {
    const server = slowServer();
    const reader = createLogReader("m", null, server.read);

    reader.start();
    reader.catchUp();
    expect(server.waiting()).toBe(1);

    await server.next().answer(lines(1, 2));
    const catchUp = server.next();
    expect(catchUp.read).toMatchObject({ after: 2 });

    await catchUp.answer([line(3)]);
    expect(ids(reader)).toEqual([1, 2, 3]);
    expect(server.waiting()).toBe(0);
  });

  it("reads page after page until a gap longer than a page is closed", async () => {
    const server = slowServer();
    const reader = createLogReader("m", "run", server.read);

    reader.start();
    await server.next().answer([line(1)]);

    reader.catchUp();
    const first = server.next();
    expect(first.read).toEqual({ after: 1, limit: LOG_PAGE_LINES, deploymentId: "run" });
    await first.answer(lines(2, LOG_PAGE_LINES + 1));

    const second = server.next();
    expect(second.read).toEqual({
      after: LOG_PAGE_LINES + 1,
      limit: LOG_PAGE_LINES,
      deploymentId: "run",
    });
    await second.answer([line(LOG_PAGE_LINES + 2)]);

    expect(server.waiting()).toBe(0);
    expect(ids(reader)).toHaveLength(LOG_PAGE_LINES + 2);
  });

  it("drops answers to reads from before it was closed or started again", async () => {
    const server = slowServer();
    const reader = createLogReader("m", null, server.read);

    reader.start();
    const early = server.next();
    reader.close();
    reader.start();
    const current = server.next();

    await current.answer([line(10)]);
    await early.answer([line(1)]);
    expect(ids(reader)).toEqual([10]);

    reader.catchUp();
    const late = server.next();
    reader.close();
    await late.answer([line(11)]);
    expect(ids(reader)).toEqual([10]);
  });

  it("reads older lines only as far as the buffer has room", async () => {
    const server = slowServer();
    const reader = createLogReader("m", null, server.read);
    const held = MAX_BUFFERED_LINES - 200;

    reader.start();
    await server.next().answer(lines(1_001, 1_000 + held), true);

    reader.loadOlder();
    const older = server.next();
    expect(older.read).toEqual({ before: 1_001, limit: 200, deploymentId: null });
    await older.answer(lines(801, 1_000), true);

    reader.loadOlder();
    expect(server.waiting()).toBe(0);
  });
});
