// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ApiError } from "./api";
import { createAutosaver, retryDelay, type AutosaveOptions, type Revised } from "./autosave";

interface Doc {
  text: string;
}

interface Call {
  value: Doc;
  revision: number;
  keepalive: boolean;
  resolve: (saved: Revised<Doc>) => void;
  reject: (error: unknown) => void;
}

// A server that answers each save when the test says so.
function saver(options: Partial<AutosaveOptions<Doc, Revised<Doc>>> = {}) {
  const calls: Call[] = [];
  const onConflict = vi.fn();
  const autosaver = createAutosaver<Doc, Revised<Doc>>({
    initial: { value: { text: "a" }, revision: 1 },
    save: (value, revision, keepalive) =>
      new Promise((resolve, reject) => {
        calls.push({ value, revision, keepalive, resolve, reject });
      }),
    savedAs: (saved) => saved,
    equals: (a, b) => a.text === b.text,
    conflicts: true,
    onConflict,
    ...options,
  });

  const call = (index: number): Call => {
    const found = calls[index];

    if (found === undefined) {
      throw new Error(`There is no save ${String(index + 1)}.`);
    }

    return found;
  };

  // The server stores what was sent and raises the revision.
  const accept = async (index: number) => {
    const sent = call(index);
    sent.resolve({ value: sent.value, revision: sent.revision + 1 });
    await vi.advanceTimersByTimeAsync(0);
  };

  const refuse = async (index: number, error: unknown) => {
    call(index).reject(error);
    await vi.advanceTimersByTimeAsync(0);
  };

  const type = (text: string) => {
    autosaver.update(() => ({ text }));
  };

  return { autosaver, calls, call, accept, refuse, type, onConflict };
}

const state = (autosaver: ReturnType<typeof saver>["autosaver"]) => autosaver.snapshot().state.kind;

describe("createAutosaver", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("saves once after a pause in typing", async () => {
    const { autosaver, calls, call, accept, type } = saver();

    type("b");
    await vi.advanceTimersByTimeAsync(300);
    type("bc");
    expect(state(autosaver)).toBe("pending");
    await vi.advanceTimersByTimeAsync(699);
    expect(calls).toHaveLength(0);

    await vi.advanceTimersByTimeAsync(1);
    expect(calls).toHaveLength(1);
    expect(call(0)).toMatchObject({ value: { text: "bc" }, revision: 1, keepalive: false });
    expect(state(autosaver)).toBe("saving");

    await accept(0);
    expect(autosaver.snapshot()).toMatchObject({ dirty: false, state: { kind: "saved" } });
  });

  it("saves at least every 3 s while typing goes on", async () => {
    const { calls, type } = saver();

    for (let i = 0; i < 6; i++) {
      type(`text ${String(i)}`);
      await vi.advanceTimersByTimeAsync(500);
    }

    expect(calls).toHaveLength(1);
    expect(calls[0]?.value).toEqual({ text: "text 5" });
  });

  it("sends one save at a time, and an edit made meanwhile next with the new revision", async () => {
    const { calls, call, accept, type } = saver();

    type("b");
    await vi.advanceTimersByTimeAsync(700);
    type("c");
    await vi.advanceTimersByTimeAsync(5_000);
    expect(calls).toHaveLength(1);

    await accept(0);
    await vi.advanceTimersByTimeAsync(700);

    expect(calls).toHaveLength(2);
    expect(call(1)).toMatchObject({ value: { text: "c" }, revision: 2 });
  });

  it("saves a change of the structure at once", async () => {
    const { autosaver, calls } = saver();

    autosaver.update(() => ({ text: "moved" }), true);
    await vi.advanceTimersByTimeAsync(0);

    expect(calls).toHaveLength(1);
  });

  it("stops at a conflict and saves nothing until a copy is chosen", async () => {
    const { autosaver, calls, call, refuse, type, onConflict } = saver();

    type("mine");
    await vi.advanceTimersByTimeAsync(700);
    await refuse(0, new ApiError(409, "Conflict"));

    expect(state(autosaver)).toBe("conflict");
    expect(onConflict).toHaveBeenCalledOnce();

    type("mine, more");
    await vi.advanceTimersByTimeAsync(10_000);
    expect(calls).toHaveLength(1);

    autosaver.receive({ text: "theirs" }, 5);
    expect(autosaver.snapshot().theirs).toEqual({ value: { text: "theirs" }, revision: 5 });

    autosaver.keepMine();
    await vi.advanceTimersByTimeAsync(0);

    expect(calls).toHaveLength(2);
    expect(call(1)).toMatchObject({ value: { text: "mine, more" }, revision: 5 });
  });

  it("throws the unsaved edits away for their copy", async () => {
    const { autosaver, calls, refuse, type } = saver();

    type("mine");
    await vi.advanceTimersByTimeAsync(700);
    await refuse(0, new ApiError(409, "Conflict"));
    autosaver.receive({ text: "theirs" }, 5);

    autosaver.takeTheirs();
    await vi.advanceTimersByTimeAsync(10_000);

    expect(autosaver.snapshot()).toMatchObject({
      value: { text: "theirs" },
      dirty: false,
      theirs: null,
      state: { kind: "saved" },
    });
    expect(calls).toHaveLength(1);
  });

  it("tries again after a network error, waiting longer each time", async () => {
    const { autosaver, calls, accept, refuse, type } = saver();

    type("b");
    await vi.advanceTimersByTimeAsync(700);
    await refuse(0, new TypeError("Failed to fetch"));

    expect(autosaver.snapshot().state).toMatchObject({ kind: "retrying", attempt: 1 });
    await vi.advanceTimersByTimeAsync(retryDelay(1) - 1);
    expect(calls).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(calls).toHaveLength(2);

    await refuse(1, new ApiError(503, "Service Unavailable"));
    await vi.advanceTimersByTimeAsync(retryDelay(2) - 1);
    expect(calls).toHaveLength(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(calls).toHaveLength(3);

    await accept(2);
    expect(state(autosaver)).toBe("saved");
    expect([retryDelay(1), retryDelay(2), retryDelay(9)]).toEqual([1_000, 2_000, 30_000]);
  });

  it("does not take a copy read during its own save for someone else's", async () => {
    const { autosaver, accept, type } = saver();

    type("b");
    await vi.advanceTimersByTimeAsync(700);

    // The push of this very save arrives before its answer.
    autosaver.receive({ text: "b" }, 2);
    await accept(0);

    expect(autosaver.snapshot()).toMatchObject({ dirty: false, state: { kind: "saved" } });
  });

  it("takes another copy live while nothing is unsaved, and calls it a conflict otherwise", () => {
    const { autosaver, type } = saver();

    autosaver.receive({ text: "from another page" }, 2);
    expect(autosaver.snapshot()).toMatchObject({
      value: { text: "from another page" },
      dirty: false,
    });

    type("mine");
    autosaver.receive({ text: "newer still" }, 3);

    expect(autosaver.snapshot()).toMatchObject({
      value: { text: "mine" },
      state: { kind: "conflict" },
      theirs: { value: { text: "newer still" }, revision: 3 },
    });
  });

  it("waits for the next edit after the server refused a value", async () => {
    const { autosaver, calls, refuse, type } = saver();
    const problem = { title: "Invalid", errors: { name: ["Enter a name."] } };

    type("");
    await vi.advanceTimersByTimeAsync(700);
    await refuse(0, new ApiError(400, "Enter a name.", problem));

    expect(autosaver.snapshot().state).toEqual({
      kind: "refused",
      message: "Enter a name.",
      problem,
    });
    await vi.advanceTimersByTimeAsync(10_000);
    expect(calls).toHaveLength(1);

    type("Lab");
    await vi.advanceTimersByTimeAsync(700);
    expect(calls).toHaveLength(2);
  });

  it("treats a 409 as a refusal when the document has no revisions", async () => {
    const { autosaver, refuse, type } = saver({ conflicts: false });

    type("00155D010203");
    await vi.advanceTimersByTimeAsync(700);
    await refuse(0, new ApiError(409, "There is a rule for MAC 00:15:5D:01:02:03 already."));

    expect(state(autosaver)).toBe("refused");
  });

  it("stops saving when the document is gone", async () => {
    const { autosaver, calls, refuse, type } = saver();

    type("b");
    await vi.advanceTimersByTimeAsync(700);
    await refuse(0, new ApiError(404, "Not Found"));
    type("c");
    await vi.advanceTimersByTimeAsync(10_000);

    expect(autosaver.snapshot().state).toEqual({
      kind: "stopped",
      message: "It no longer exists on the server, so nothing more is saved.",
    });
    expect(calls).toHaveLength(1);
  });

  it("sends at once when asked to flush, and says whether everything was saved", async () => {
    const { autosaver, calls, accept, type } = saver();

    type("b");
    const flushed = autosaver.flush();
    await vi.advanceTimersByTimeAsync(0);
    expect(calls).toHaveLength(1);

    await accept(0);
    await expect(flushed).resolves.toBe(true);
    await expect(autosaver.flush()).resolves.toBe(true);
  });

  it("says nothing was saved when a flush finds a conflict", async () => {
    const { autosaver, refuse, type } = saver();

    type("b");
    const flushed = autosaver.flush();
    await vi.advanceTimersByTimeAsync(0);
    await refuse(0, new ApiError(409, "Conflict"));

    await expect(flushed).resolves.toBe(false);
  });

  it("sends what is not saved yet when closed, and schedules nothing after", async () => {
    const { autosaver, calls, refuse, type } = saver();

    type("b");
    autosaver.close();
    await vi.advanceTimersByTimeAsync(0);
    expect(calls).toHaveLength(1);

    await refuse(0, new TypeError("Failed to fetch"));
    await vi.advanceTimersByTimeAsync(60_000);
    expect(calls).toHaveLength(1);
  });

  it("sends with keepalive as the page is closed", async () => {
    const { autosaver, call, type } = saver();

    type("b");
    await expect(autosaver.flush(true)).resolves.toBe(false);
    await vi.advanceTimersByTimeAsync(0);

    expect(call(0).keepalive).toBe(true);
  });
});
