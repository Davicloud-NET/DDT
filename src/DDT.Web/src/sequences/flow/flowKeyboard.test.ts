// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { branch, draftOfSteps, group, leaf, repeat } from "@/test/trees";

import { flowEdits } from "./flowEdits";
import {
  afterRemoval,
  clipboardText,
  flowCommand,
  flowTarget,
  nodeLabel,
  nodesFromClipboard,
  shiftEdit,
  slotAfter,
  slotLabel,
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
    // The last node of a branch goes on past the join.
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

describe("what a screen reader hears", () => {
  const named = indexTree([
    leaf("a"),
    {
      ...branch("b", [leaf("c"), { ...leaf("d"), kind: "reboot", name: "Restart" } as never], []),
      name: "Is it a Latitude?",
    },
    group("f"),
  ]);

  it("says where a node is, its name, its kind and its findings", () => {
    expect(nodeLabel(named, "a", 0, 0)).toBe("Step 1, Step a, Run script");
    expect(nodeLabel(named, "c", 1, 0)).toBe(
      "Step 1 of Then of 'If: Is it a Latitude?', Step c, Run script, 1 problem",
    );
    expect(nodeLabel(named, "b", 0, 2)).toBe("Step 2, If: Is it a Latitude?, 2 warnings");
  });

  it("says where a gap is", () => {
    expect(slotLabel(named, { parent: null, body: "steps", index: 0 })).toBe(
      "Add a step before Step a",
    );
    expect(slotLabel(named, { parent: null, body: "steps", index: 1 })).toBe(
      "Add a step between Step a and If: Is it a Latitude?",
    );
    expect(slotLabel(named, { parent: null, body: "steps", index: 3 })).toBe(
      "Add a step after Group: Group f",
    );
    expect(slotLabel(named, { parent: "b", body: "else", index: 0 })).toBe(
      "Add a step to Else of 'If: Is it a Latitude?'",
    );
    expect(slotLabel(named, { parent: "f", body: "steps", index: 0 })).toBe(
      "Add a step to 'Group: Group f'",
    );
    expect(slotLabel(indexTree([]), { parent: null, body: "steps", index: 0 })).toBe(
      "Add the first step",
    );
  });
});

describe("the clipboard", () => {
  it("holds nodes as DDT's and gives them back", () => {
    const text = clipboardText([steps[1] ?? leaf("x")]);

    expect(JSON.parse(text)).toMatchObject({ ddtFlow: 1, nodes: [{ id: "b", kind: "if" }] });
    expect(nodesFromClipboard(text)?.map((node) => node.id)).toEqual(["b"]);
  });

  it("refuses other text", () => {
    expect(nodesFromClipboard("Apply image")).toBeNull();
    expect(nodesFromClipboard(JSON.stringify({ ddtFlow: 2, nodes: [leaf("a")] }))).toBeNull();
    expect(nodesFromClipboard(JSON.stringify({ ddtFlow: 1, nodes: [] }))).toBeNull();
    expect(
      nodesFromClipboard(JSON.stringify({ ddtFlow: 1, nodes: [{ ...leaf("a"), kind: "format" }] })),
    ).toBeNull();
    expect(
      nodesFromClipboard(
        JSON.stringify({ ddtFlow: 1, nodes: [{ ...group("g"), steps: [{ id: "x" }] }] }),
      ),
    ).toBeNull();
  });
});
