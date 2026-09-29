// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { branch, draftOfSteps, group, leaf, repeat } from "@/test/trees";

import { flowEdits } from "./flowEdits";
import {
  afterRemoval,
  flowCommand,
  flowTarget,
  shiftEdit,
  slotAfter,
  type FlowMove,
} from "./flowKeyboard";
import { indexTree } from "./flowTree";

// a, IF b (Then c, d; Else e), group f (repeat g (h)), i
const steps = [
  leaf("a"),
  branch("b", [leaf("c"), leaf("d")], [leaf("e")]),
  group("f", repeat("g", leaf("h"))),
  leaf("i"),
];
const index = indexTree(steps);

function go(from: string, move: FlowMove, collapsed: string[] = []): string | null {
  return flowTarget(index, from, move, new Set(collapsed));
}

function key(
  key: string,
  modifiers: Partial<Record<"ctrl" | "shift" | "alt" | "meta", boolean>> = {},
) {
  return flowCommand({
    key,
    ctrlKey: modifiers.ctrl === true,
    shiftKey: modifiers.shift === true,
    altKey: modifiers.alt === true,
    metaKey: modifiers.meta === true,
  });
}

describe("the flow's keys", () => {
  it("give a command for each key the canvas takes, and leave the rest alone", () => {
    expect(key("ArrowDown")).toEqual({ type: "move", move: "down" });
    expect(key("Escape")).toEqual({ type: "move", move: "parent" });
    expect(key("Home")).toEqual({ type: "move", move: "home" });
    expect(key("Enter")).toEqual({ type: "open" });
    expect(key("Delete")).toEqual({ type: "remove" });
    expect(key("ArrowUp", { alt: true })).toEqual({ type: "shift", by: -1 });
    expect(key("c", { ctrl: true })).toEqual({ type: "copy" });
    expect(key("X", { meta: true })).toEqual({ type: "cut" });
    expect(key("v", { ctrl: true })).toEqual({ type: "paste" });
    expect(key("d", { ctrl: true })).toEqual({ type: "duplicate" });
    expect(key("F10", { shift: true })).toEqual({ type: "menu" });
    expect(key("ContextMenu")).toEqual({ type: "menu" });
    // Undo and the zoom keys belong to the page and the viewport.
    expect(key("z", { ctrl: true })).toBeNull();
    expect(key("+")).toBeNull();
    expect(key("ArrowDown", { shift: true })).toBeNull();
  });
});

describe("moving through the flow", () => {
  it("goes down the way the flow runs: into Then, past the join, into a container", () => {
    expect(go("a", "down")).toBe("b");
    expect(go("b", "down")).toBe("c");
    expect(go("c", "down")).toBe("d");
    // The last node of a branch continues past the join.
    expect(go("d", "down")).toBe("f");
    expect(go("e", "down")).toBe("f");
    expect(go("f", "down")).toBe("g");
    expect(go("g", "down")).toBe("h");
    expect(go("h", "down")).toBe("i");
    expect(go("i", "down")).toBeNull();
    // A collapsed container is passed as one node.
    expect(go("f", "down", ["f"])).toBe("i");
  });

  it("goes up the same way back, and out to the container at the start of a list", () => {
    expect(go("i", "up")).toBe("h");
    expect(go("i", "up", ["f"])).toBe("f");
    expect(go("f", "up")).toBe("d");
    expect(go("c", "up")).toBe("b");
    expect(go("e", "up")).toBe("b");
    expect(go("a", "up")).toBeNull();
  });

  it("goes across to the other branch of the nearest IF", () => {
    expect(go("c", "right")).toBe("e");
    expect(go("d", "right")).toBe("e");
    expect(go("e", "left")).toBe("c");
    expect(go("c", "left")).toBeNull();
    expect(go("a", "right")).toBeNull();
  });

  it("goes to the ends and out to the container", () => {
    expect(go("h", "home")).toBe("a");
    expect(go("c", "end")).toBe("i");
    expect(go("h", "parent")).toBe("g");
    expect(go("g", "parent")).toBe("f");
    expect(go("a", "parent")).toBeNull();
  });
});

describe("editing from the keys", () => {
  it("trades places with a neighbour within the list", () => {
    const down = shiftEdit(index, "c", 1);
    const up = shiftEdit(index, "d", -1);

    expect(down).not.toBeNull();
    expect(up).not.toBeNull();

    const moved = (edit: typeof down) =>
      edit === null
        ? []
        : (flowEdits(draftOfSteps(...steps), edit).steps[1] as ReturnType<typeof branch>).then.map(
            (node) => node.id,
          );

    expect(moved(down)).toEqual(["d", "c"]);
    expect(moved(up)).toEqual(["d", "c"]);
    expect(shiftEdit(index, "d", 1)).toBeNull();
    expect(shiftEdit(index, "a", -1)).toBeNull();
  });

  it("pastes after the node, and at the end without one", () => {
    expect(slotAfter(index, "c")).toEqual({ parent: "b", body: "then", index: 1 });
    expect(slotAfter(index, null)).toEqual({ parent: null, body: "steps", index: 4 });
  });

  it("gives the focus to the node that takes a removed one's place", () => {
    expect(afterRemoval(index, "c")).toBe("d");
    expect(afterRemoval(index, "d")).toBe("c");
    expect(afterRemoval(index, "e")).toBe("b");
    expect(afterRemoval(index, "i")).toBe("f");
  });
});
