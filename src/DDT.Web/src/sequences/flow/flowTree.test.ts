// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { branch, group, leaf, repeat } from "@/test/trees";

import type { SequenceStep } from "../sequences";
import { newStep } from "../steps";
import {
  ancestorsOf,
  bodyOf,
  findNode,
  indexTree,
  isWithin,
  listAt,
  requiredVersion,
  slotOf,
  slotsOf,
  walk,
} from "./flowTree";

// a, IF b (then c, group d (e); else f), repeat g (h), i
const tree: SequenceStep[] = [
  leaf("a"),
  branch("b", [leaf("c"), group("d", leaf("e"))], [leaf("f")]),
  repeat("g", leaf("h")),
  leaf("i"),
];

describe("walk", () => {
  it("goes in pre-order, Then before Else, with each node's place", () => {
    expect(
      walk(tree).map((entry) => [
        entry.node.id,
        entry.parent,
        entry.body,
        entry.index,
        entry.depth,
        entry.order,
      ]),
    ).toEqual([
      ["a", null, "steps", 0, 0, 0],
      ["b", null, "steps", 1, 0, 1],
      ["c", "b", "then", 0, 1, 2],
      ["d", "b", "then", 1, 1, 3],
      ["e", "d", "steps", 0, 2, 4],
      ["f", "b", "else", 0, 1, 5],
      ["g", null, "steps", 2, 0, 6],
      ["h", "g", "steps", 0, 1, 7],
      ["i", null, "steps", 3, 0, 8],
    ]);
  });

  it("numbers the leaves from 1 in document order, and not the containers", () => {
    expect(walk(tree).map((entry) => [entry.node.id, entry.number])).toEqual([
      ["a", 1],
      ["b", null],
      ["c", 2],
      ["d", null],
      ["e", 3],
      ["f", 4],
      ["g", null],
      ["h", 5],
      ["i", 6],
    ]);
  });

  it("keeps the first node where an id repeats", () => {
    const index = indexTree([leaf("a"), group("g", { ...leaf("a"), name: "Again" })]);

    expect(index.entries).toHaveLength(3);
    expect(index.byId.get("a")?.node.name).toBe("Step a");
  });
});

describe("finding in the tree", () => {
  it("finds a node at any depth, and the list a slot names", () => {
    expect(findNode(tree, "e")?.name).toBe("Step e");
    expect(findNode(tree, "x")).toBeUndefined();
    expect(listAt(tree, null, "steps")).toBe(tree);
    expect(listAt(tree, "b", "else")?.map((node) => node.id)).toEqual(["f"]);
    expect(listAt(tree, "b", "steps")).toBeUndefined();
    expect(listAt(tree, "a", "steps")).toBeUndefined();
    expect(listAt(tree, null, "then")).toBeUndefined();
    expect(bodyOf(leaf("x"), "steps")).toBeUndefined();
  });

  it("names a node's containers, its parent first", () => {
    const index = indexTree(tree);

    expect(ancestorsOf(index, "e")).toEqual(["d", "b"]);
    expect(ancestorsOf(index, "a")).toEqual([]);
    expect(isWithin(index, "e", "b")).toBe(true);
    expect(isWithin(index, "b", "b")).toBe(true);
    expect(isWithin(index, "b", "e")).toBe(false);
    expect(slotOf(index, "f")).toEqual({ parent: "b", body: "else", index: 0 });
  });
});

describe("slotsOf", () => {
  it("has every gap of every list in document order, one in an empty body", () => {
    const slots = slotsOf([leaf("a"), branch("b", [leaf("c")]), group("d")]);

    expect(slots).toEqual([
      { parent: null, body: "steps", index: 0 },
      { parent: null, body: "steps", index: 1 },
      { parent: "b", body: "then", index: 0 },
      { parent: "b", body: "then", index: 1 },
      { parent: "b", body: "else", index: 0 },
      { parent: null, body: "steps", index: 2 },
      { parent: "d", body: "steps", index: 0 },
      { parent: null, body: "steps", index: 3 },
    ]);
    expect(slotsOf([])).toEqual([{ parent: null, body: "steps", index: 0 }]);
  });
});

describe("requiredVersion", () => {
  const script = leaf("s");

  it("is 1 for Windows steps and 2 with a raw disk image or a seed", () => {
    expect(requiredVersion({ steps: [script, newStep("reboot", "r")] })).toBe(1);
    expect(requiredVersion({ steps: [script, newStep("writeCloudInitSeed", "c")] })).toBe(2);
    expect(requiredVersion({ steps: [script], variables: [], inputs: null })).toBe(1);
  });

  it("is 3 for anything of the tree", () => {
    const three: { steps: SequenceStep[]; variables?: []; inputs?: [] }[] = [
      { steps: [group("g")] },
      { steps: [branch("b", [])] },
      { steps: [repeat("r")] },
      { steps: [newStep("setVariable", "v")] },
      { steps: [newStep("pause", "p")] },
      { steps: [{ ...script, when: { kind: "all", parts: [] } }] },
      {
        steps: [
          { ...script, shares: [{ path: "\\\\h\\s", account: { accountId: "x", input: null } }] },
        ],
      },
      { steps: [{ ...script, runAs: { accountId: null, input: "Installer" } }] },
      {
        steps: [
          {
            ...newStep("joinDomain", "j"),
            account: { accountId: null, input: "Join" },
          } as SequenceStep,
        ],
      },
      {
        steps: [
          { ...script, conditions: [{ variable: "Model", operator: "Matches", value: "x*" }] },
        ],
      },
      {
        steps: [
          { ...script, conditions: [{ variable: "DeviceKind", operator: "Equals", value: "x" }] },
        ],
      },
    ];

    expect(three.map(requiredVersion)).toEqual(three.map(() => 3));
    expect(
      requiredVersion({
        steps: [script],
        variables: [{ name: "Office", default: null, description: null, setBySteps: false }],
      }),
    ).toBe(3);
  });

  it("is 1 for a flat step whose optional members are unset or empty", () => {
    expect(
      requiredVersion({
        steps: [
          { ...script, when: null, shares: [], runAs: null },
          {
            ...script,
            id: "t",
            conditions: [{ variable: "Model", operator: "Contains", value: "x" }],
          },
        ],
      }),
    ).toBe(1);
  });
});
