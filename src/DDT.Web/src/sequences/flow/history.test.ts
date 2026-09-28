// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { isTextField } from "@/lib/textField";

import {
  emptyHistory,
  HISTORY_DEPTH,
  historyCommand,
  recorded,
  redone,
  undone,
  type History,
} from "./history";

// Edits a text and keeps the history the same way the editor does.
function edits(...steps: [string, string | null, number][]) {
  let value = "";
  let history: History<string> = emptyHistory();

  for (const [next, key, at] of steps) {
    history = recorded(history, value, key, at);
    value = next;
  }

  return { value, history };
}

function undoAll(start: { value: string; history: History<string> }): string[] {
  const seen: string[] = [];
  let { value, history } = start;

  for (;;) {
    const stepped = undone(history, value);

    if (stepped === null) {
      return seen;
    }

    ({ value, history } = stepped);
    seen.push(value);
  }
}

describe("history", () => {
  it("goes back and forth through the copies before each edit", () => {
    const start = edits(["a", null, 0], ["ab", null, 10], ["abc", null, 20]);

    expect(undoAll(start)).toEqual(["ab", "a", ""]);

    const back = undone(start.history, start.value);
    const forth = back === null ? null : redone(back.history, back.value);

    expect(back?.value).toBe("ab");
    expect(forth?.value).toBe("abc");
    expect(forth === null ? null : redone(forth.history, forth.value)).toBeNull();
  });

  it("takes back typing into one field in a row as one step, while no second passes between keys", () => {
    const typed = edits(
      ["L", "node:a:name", 0],
      ["La", "node:a:name", 900],
      ["Lab", "node:a:name", 1_800],
      ["Lab!", "node:a:script", 1_900],
      ["Lab!?", "node:a:script", 3_000],
    );

    expect(undoAll(typed)).toEqual(["Lab!", "Lab", ""]);
  });

  it("starts a new step for typing after an undo or a change of the structure", () => {
    const typed = edits(
      ["a", "name", 0],
      ["b", null, 100],
      ["bc", "name", 200],
      ["bcd", "name", 300],
    );

    expect(undoAll(typed)).toEqual(["b", "a", ""]);

    const back = undone(typed.history, typed.value);

    if (back === null) {
      throw new Error("Nothing to undo.");
    }

    const again = recorded(back.history, back.value, "name", 400);

    expect(again.past).toEqual(["", "a", "b"]);
    expect(again.future).toEqual([]);
  });

  it("keeps the last 100 steps", () => {
    let history: History<number> = emptyHistory();

    for (let edit = 0; edit < 150; edit++) {
      history = recorded(history, edit, null, edit);
    }

    expect(history.past).toHaveLength(HISTORY_DEPTH);
    expect(history.past[0]).toBe(50);
  });
});

describe("historyCommand", () => {
  const press = (
    key: string,
    keys: { ctrl?: boolean; meta?: boolean; shift?: boolean; alt?: boolean },
  ) =>
    historyCommand({
      key,
      ctrlKey: keys.ctrl ?? false,
      metaKey: keys.meta ?? false,
      shiftKey: keys.shift ?? false,
      altKey: keys.alt ?? false,
    });

  it("reads Ctrl or ⌘ with Z as undo, and with Y or Shift+Z as redo", () => {
    expect(press("z", { ctrl: true })).toBe("undo");
    expect(press("z", { meta: true })).toBe("undo");
    expect(press("Z", { ctrl: true, shift: true })).toBe("redo");
    expect(press("y", { ctrl: true })).toBe("redo");
    expect(press("z", {})).toBeNull();
    expect(press("z", { ctrl: true, alt: true })).toBeNull();
    expect(press("z", { ctrl: true, meta: true })).toBeNull();
    expect(press("x", { ctrl: true })).toBeNull();
  });

  it("leaves keys in a text field to the field", () => {
    const field = (html: string) => {
      document.body.innerHTML = html;

      return document.body.querySelector("[data-target]");
    };

    expect(isTextField(field('<input data-target type="text">'))).toBe(true);
    expect(isTextField(field('<input data-target type="search">'))).toBe(true);
    expect(isTextField(field("<textarea data-target></textarea>"))).toBe(true);
    expect(isTextField(field('<div contenteditable="true"><b data-target>x</b></div>'))).toBe(true);
    expect(isTextField(field('<input data-target type="checkbox">'))).toBe(false);
    expect(isTextField(field("<button data-target>Add</button>"))).toBe(false);
    expect(isTextField(field('<div contenteditable="false"><b data-target>x</b></div>'))).toBe(
      false,
    );
    expect(isTextField(null)).toBe(false);
  });
});
