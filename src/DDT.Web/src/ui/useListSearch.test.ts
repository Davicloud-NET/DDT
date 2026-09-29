// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { useListSearch } from "./useListSearch";

const machines = ["PC-042", "Lab-PC-7", "Kiosk"];

function renderSearch(items: string[] = machines) {
  const needles: string[] = [];
  const rendered = renderHook(() =>
    useListSearch(items, (item, needle) => {
      needles.push(needle);
      return item.toLowerCase().includes(needle);
    }),
  );

  const type = (query: string) => {
    act(() => {
      rendered.result.current.setQuery(query);
    });
  };

  return { ...rendered, needles, type };
}

describe("useListSearch", () => {
  it("shows the whole list, as it is, before anything is typed", () => {
    const { result } = renderSearch();

    expect(result.current.query).toBe("");
    expect(result.current.shown).toBe(machines);
  });

  it("keeps the items that match what is typed", () => {
    const { result, type } = renderSearch();

    type("pc");

    expect(result.current.query).toBe("pc");
    expect(result.current.shown).toEqual(["PC-042", "Lab-PC-7"]);
  });

  it("hands matches the search in lower case, without the spaces around it", () => {
    const { needles, type } = renderSearch();

    type("  KIOSK ");

    expect(new Set(needles)).toEqual(new Set(["kiosk"]));
  });

  it("shows the whole list again for a search of only spaces", () => {
    const { result, type } = renderSearch();

    type("   ");

    expect(result.current.shown).toBe(machines);
  });
});
