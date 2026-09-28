// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { ApiError } from "./api";
import type { AutosaveState } from "./autosave";
import {
  classifyFailure,
  dueIn,
  flushOutcome,
  reconcile,
  type KnownCopy,
  type Reconciliation,
} from "./autosaveDecisions";

function refusal(status: number): ApiError {
  return new ApiError(status, `Refused with ${String(status)}.`);
}

describe("classifyFailure", () => {
  it.each<[string, unknown, boolean, string]>([
    ["a newer revision where 409 means one", refusal(409), true, "conflict"],
    ["a refused value where 409 means none", refusal(409), false, "refused"],
    ["a sign-out", refusal(401), true, "stopped"],
    ["a missing permission", refusal(403), true, "stopped"],
    ["a deleted document", refusal(404), true, "stopped"],
    ["a refused value", refusal(400), true, "refused"],
    ["a timeout", refusal(408), true, "retry"],
    ["too many requests", refusal(429), true, "retry"],
    ["a server error", refusal(503), true, "retry"],
    ["no answer at all", new TypeError("Failed to fetch"), true, "retry"],
  ])("takes %s as %s", (_, error, conflicts, kind) => {
    expect(classifyFailure(error, conflicts).kind).toBe(kind);
  });

  it("says why saving stopped, and that a server that did not answer is retried", () => {
    expect(classifyFailure(refusal(404), true)).toEqual({
      kind: "stopped",
      message: "It no longer exists on the server, so nothing more is saved.",
    });
    expect(classifyFailure(new TypeError("Failed to fetch"), true)).toEqual({
      kind: "retry",
      message: "The server did not answer.",
    });
  });
});

describe("reconcile", () => {
  const known = (overrides: Partial<KnownCopy<string>> = {}): KnownCopy<string> => ({
    revision: 3,
    echo: "saved",
    state: "saved",
    dirty: false,
    ...overrides,
  });

  it.each<[string, number, string, Partial<KnownCopy<string>>, boolean, Reconciliation]>([
    ["an older copy", 2, "old", {}, true, "ignore"],
    ["the echo of the page's own save", 3, "saved", {}, true, "ignore"],
    ["another form of the same revision while nothing is unsaved", 3, "other", {}, true, "adopt"],
    ["a newer copy while nothing is unsaved", 4, "new", {}, true, "adopt"],
    ["a newer copy while edits are unsaved", 4, "new", { dirty: true }, true, "conflict"],
    ["a newer copy where the last save wins", 4, "new", { dirty: true }, false, "ignore"],
    [
      "a newer copy during a conflict",
      4,
      "new",
      { state: "conflict", dirty: true },
      true,
      "theirs",
    ],
    ["the same revision during a conflict", 3, "other", { state: "conflict" }, true, "ignore"],
    ["a newer copy once saving stopped", 4, "new", { state: "stopped" }, true, "ignore"],
  ])("does with %s: %s", (_, revision, value, overrides, conflicts, expected) => {
    const equals = (a: string, b: string) => a === b;

    expect(reconcile({ value, revision }, known(overrides), { conflicts, equals })).toBe(expected);
  });
});

describe("dueIn", () => {
  it.each<[string, number | null, number, number, number]>([
    ["waits for a pause after the last edit", 1_000, 1_000, 1_000, 700],
    ["counts the pause from the last edit", 0, 1_000, 1_200, 500],
    ["saves at the latest after the longest wait", 0, 2_800, 2_900, 100],
    ["saves at once when the longest wait is over", 0, 3_500, 3_600, 0],
    ["takes now as the first edit without one", null, 1_000, 1_000, 700],
  ])("%s", (_, firstEditAt, lastEditAt, now, expected) => {
    expect(dueIn(firstEditAt, lastEditAt, now, 700, 3_000)).toBe(expected);
  });
});

describe("flushOutcome", () => {
  it.each<[boolean, AutosaveState["kind"], boolean | null]>([
    [false, "saved", true],
    [false, "conflict", true],
    [true, "pending", null],
    [true, "saving", null],
    [true, "retrying", false],
    [true, "refused", false],
    [true, "conflict", false],
    [true, "stopped", false],
  ])("answers a flush while dirty is %s and the state %s with %s", (dirty, state, expected) => {
    expect(flushOutcome(dirty, state)).toBe(expected);
  });
});
