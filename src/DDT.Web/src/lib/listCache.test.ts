// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { removeByIds, upsertById } from "./listCache";

interface Item {
  id: string;
  name: string;
}

const byName = (a: Item, b: Item) => a.name.localeCompare(b.name);
const list: Item[] = [
  { id: "1", name: "Alpha" },
  { id: "2", name: "Delta" },
];

describe("upsertById", () => {
  it.each<[string, Item, string[]]>([
    ["adds a new item in order", { id: "3", name: "Bravo" }, ["1:Alpha", "3:Bravo", "2:Delta"]],
    ["replaces the older copy", { id: "2", name: "Charlie" }, ["1:Alpha", "2:Charlie"]],
    [
      "puts the item before those equal to it",
      { id: "3", name: "Alpha" },
      ["3:Alpha", "1:Alpha", "2:Delta"],
    ],
  ])("%s", (_, item, expected) => {
    expect(upsertById(list, item, byName)?.map((entry) => `${entry.id}:${entry.name}`)).toEqual(
      expected,
    );
  });

  it("leaves a list that was never read alone", () => {
    expect(upsertById(undefined, { id: "3", name: "Bravo" }, byName)).toBeUndefined();
  });
});

describe("removeByIds", () => {
  it("drops the items with the ids and keeps the order of the rest", () => {
    expect(removeByIds(list, ["1", "9"])).toEqual([{ id: "2", name: "Delta" }]);
    expect(removeByIds(undefined, ["1"])).toBeUndefined();
  });
});
