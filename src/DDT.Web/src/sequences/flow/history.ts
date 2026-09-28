// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Undo and redo of a document edited in place, as copies of the whole document: each edit keeps the copy before it.
// Typing into one field in a row is one step, as long as no second passes between keys. An undo is saved like any
// edit. When the page takes in a copy someone else saved, the steps no longer lead anywhere and are dropped.

export const HISTORY_DEPTH = 100;

export const TYPING_JOINS_MS = 1_000;

export interface History<T> {
  // The copies before each edit, oldest first, and the copies an undo left, the next to redo last.
  past: T[];
  future: T[];
  // The field typed into last and when, so typing on in it joins that step.
  typing: { key: string; at: number } | null;
}

export function emptyHistory<T>(): History<T> {
  return { past: [], future: [], typing: null };
}

// The history after an edit that changed before into something else. key names the field typed into, as typingKey
// does, and is null for every other edit. A new edit drops what there was to redo.
export function recorded<T>(
  history: History<T>,
  before: T,
  key: string | null,
  now: number,
): History<T> {
  const typing = key === null ? null : { key, at: now };
  const last = history.typing;

  if (
    key !== null &&
    last?.key === key &&
    now - last.at <= TYPING_JOINS_MS &&
    history.past.length > 0
  ) {
    return { ...history, typing };
  }

  return { past: [...history.past, before].slice(-HISTORY_DEPTH), future: [], typing };
}

export interface Stepped<T> {
  history: History<T>;
  value: T;
}

// The copy to go back to from present, or null when there is none. The copies are never undefined.
export function undone<T>(history: History<T>, present: T): Stepped<T> | null {
  const value = history.past.at(-1);

  return value === undefined
    ? null
    : {
        value,
        history: {
          past: history.past.slice(0, -1),
          future: [...history.future, present],
          typing: null,
        },
      };
}

export function redone<T>(history: History<T>, present: T): Stepped<T> | null {
  const value = history.future.at(-1);

  return value === undefined
    ? null
    : {
        value,
        history: {
          past: [...history.past, present].slice(-HISTORY_DEPTH),
          future: history.future.slice(0, -1),
          typing: null,
        },
      };
}

export type HistoryCommand = "undo" | "redo";

// Ctrl+Z (⌘Z on a Mac) undoes; Ctrl+Y and Ctrl+Shift+Z (⌘⇧Z) redo. A page leaves them to a text field it is
// pressed in (isTextField), whose own undo takes back its typing.
export function historyCommand(event: {
  key: string;
  ctrlKey: boolean;
  metaKey: boolean;
  shiftKey: boolean;
  altKey: boolean;
}): HistoryCommand | null {
  if (event.altKey || event.ctrlKey === event.metaKey) {
    return null;
  }

  const key = event.key.toLowerCase();

  if (key === "z") {
    return event.shiftKey ? "redo" : "undo";
  }

  return key === "y" && !event.shiftKey ? "redo" : null;
}
