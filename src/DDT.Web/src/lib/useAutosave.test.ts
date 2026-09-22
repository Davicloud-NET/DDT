// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { Revised } from "./autosave";
import { useAutosave } from "./useAutosave";

interface Save {
  value: string;
  revision: number;
  keepalive: boolean;
}

// A document that is one text; the server stores each save with the next revision.
function renderAutosave() {
  const saves: Save[] = [];
  const rendered = renderHook(() =>
    useAutosave<string, Revised<string>>({
      initial: { value: "a", revision: 1 },
      save: (value, revision, keepalive) => {
        saves.push({ value, revision, keepalive });
        return Promise.resolve({ value, revision: revision + 1 });
      },
      savedAs: (saved) => saved,
      equals: (a, b) => a === b,
    }),
  );

  const edit = (value: string) => {
    act(() => {
      rendered.result.current.update(() => value);
    });
  };

  return { ...rendered, saves, edit };
}

// Whether closing or reloading the page would ask first.
function asksBeforeUnload(): boolean {
  const event = new Event("beforeunload", { cancelable: true });
  window.dispatchEvent(event);

  return event.defaultPrevented;
}

describe("useAutosave", () => {
  it("sends an edit that waits for a pause when the page unmounts", () => {
    const { saves, edit, unmount } = renderAutosave();

    edit("b");
    expect(saves).toEqual([]);

    unmount();
    expect(saves).toEqual([{ value: "b", revision: 1, keepalive: false }]);
  });

  it("sends what is unsaved with keepalive when the page is hidden", () => {
    const { saves, edit, unmount } = renderAutosave();

    edit("b");
    window.dispatchEvent(new Event("pagehide"));

    expect(saves).toEqual([{ value: "b", revision: 1, keepalive: true }]);
    unmount();
  });

  it("asks before the page closes only while something is unsaved", async () => {
    const { result, edit, unmount } = renderAutosave();

    expect(asksBeforeUnload()).toBe(false);

    edit("b");
    expect(asksBeforeUnload()).toBe(true);

    await act(async () => {
      expect(await result.current.flush()).toBe(true);
    });
    expect(asksBeforeUnload()).toBe(false);

    unmount();
  });
});
