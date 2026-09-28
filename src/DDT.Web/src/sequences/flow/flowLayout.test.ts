// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// @vitest-environment node

import { describe, expect, it } from "vitest";

import everyNodeJson from "@/test/fixtures/every-node.sequence.json";
import { branch, group, leaf, repeat } from "@/test/trees";

import type { SequenceDefinition, SequenceStep } from "../sequences";
import {
  ARROW_LENGTH,
  HEADER_HEIGHT,
  IF_HEIGHT,
  LEAF_HEIGHT,
  NODE_WIDTH,
  PORT_INSET,
  type FlowLayout,
  type Point,
  type Rect,
} from "./flowGeometry";
import { layoutFlow } from "./flowLayout";
import { indexTree, isWithin, sameSlot, slotsOf } from "./flowTree";
import { arrowPath, wirePath } from "./wirePaths";

const everyNode = (everyNodeJson as unknown as SequenceDefinition).steps;

// A tree of about count nodes of every shape, the same for the same seed.
function generated(count: number, seed = 7): SequenceStep[] {
  let state = seed;
  const random = () => {
    state = (state * 1_103_515_245 + 12_345) % 2_147_483_648;

    return state / 2_147_483_648;
  };
  let made = 0;
  const id = () => `n${String(++made)}`;

  const list = (depth: number, length: number): SequenceStep[] => {
    const nodes: SequenceStep[] = [];

    while (nodes.length < length && made < count) {
      const pick = depth >= 4 ? 1 : random();

      if (pick < 0.12) {
        nodes.push(group(id(), ...list(depth + 1, Math.floor(random() * 4))));
      } else if (pick < 0.22) {
        nodes.push(
          branch(
            id(),
            list(depth + 1, Math.floor(random() * 4)),
            list(depth + 1, Math.floor(random() * 3)),
          ),
        );
      } else if (pick < 0.3) {
        nodes.push(repeat(id(), ...list(depth + 1, 1 + Math.floor(random() * 3))));
      } else {
        nodes.push(leaf(id()));
      }
    }

    return nodes;
  };

  const steps: SequenceStep[] = [];

  while (made < count) {
    steps.push(...list(0, 8));
  }

  return steps;
}

const overlaps = (a: Rect, b: Rect) =>
  a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;

const inside = (inner: Rect, outer: Rect) =>
  inner.x >= outer.x &&
  inner.y >= outer.y &&
  inner.x + inner.w <= outer.x + outer.w &&
  inner.y + inner.h <= outer.y + outer.h;

const same = (a: Point, b: Point) => Math.abs(a.x - b.x) < 0.01 && Math.abs(a.y - b.y) < 0.01;

function ends(layout: FlowLayout): Point[] {
  return layout.wires.flatMap((wire) =>
    wire.shape === "loop"
      ? [wire.points[0], wire.points.at(-1)].filter((point) => point !== undefined)
      : [wire.start, wire.end],
  );
}

// Where a wire may start or end: a card's bottom (an IF's has its ports instead), a port, a join, a frame's edge at
// its axis, an arrowhead's base, or the end of another wire. A repeat's wire back leaves and joins a straight wire.
function expectConnected(layout: FlowLayout) {
  const anchors: Point[] = [
    ...layout.boxes
      .filter((box) => box.kind !== "if")
      .map((box) => ({ x: box.x + box.w / 2, y: box.y + box.h })),
    ...layout.ports,
    ...layout.joins,
    ...layout.frames.flatMap((frame) => {
      const header = layout.boxes.find((box) => box.id === frame.id);
      const x = header === undefined ? Number.NaN : header.x + header.w / 2;

      return [
        { x, y: frame.y },
        { x, y: frame.y + frame.h },
      ];
    }),
    ...layout.arrows.map((arrow) => ({ x: arrow.x, y: arrow.y - ARROW_LENGTH })),
  ];
  const lines = layout.wires.filter((wire) => wire.shape === "line");

  layout.wires.forEach((wire, index) => {
    const own =
      wire.shape === "loop" ? [wire.points[0], wire.points.at(-1)] : [wire.start, wire.end];
    const others = ends({ ...layout, wires: layout.wires.filter((_, other) => other !== index) });

    for (const point of own) {
      if (point === undefined) {
        throw new Error("A wire has no ends.");
      }

      const onLine =
        wire.shape === "loop" &&
        lines.some(
          (other) =>
            Math.abs(other.start.x - point.x) < 0.01 &&
            point.y >= other.start.y &&
            point.y <= other.end.y,
        );

      expect({
        wire: index,
        point,
        connected: onLine || [...anchors, ...others].some((a) => same(a, point)),
      }).toEqual({
        wire: index,
        point,
        connected: true,
      });
    }
  });
}

function expectSound(steps: SequenceStep[], collapsed: ReadonlySet<string> = new Set()) {
  const layout = layoutFlow(steps, { collapsed });
  const tree = indexTree(steps);
  const hidden = (id: string) =>
    [...collapsed].some((container) => container !== id && isWithin(tree, id, container));
  const shown = tree.entries.filter((entry) => !hidden(entry.node.id));

  // One card per node that shows, of its kind's size.
  expect(layout.boxes.map((box) => box.id).sort()).toEqual(
    shown.map((entry) => entry.node.id).sort(),
  );

  for (const box of layout.boxes) {
    const height = {
      leaf: LEAF_HEIGHT,
      if: IF_HEIGHT,
      group: HEADER_HEIGHT,
      repeat: HEADER_HEIGHT,
      collapsed: HEADER_HEIGHT,
    };

    expect({ id: box.id, w: box.w, h: box.h }).toEqual({
      id: box.id,
      w: NODE_WIDTH,
      h: height[box.kind],
    });
    expect(inside(box, box.extent)).toBe(true);
    expect(inside(box.extent, { x: 0, y: 0, w: layout.width, h: layout.height })).toBe(true);
  }

  // Nothing overlaps: no two cards, and no two siblings' extents.
  layout.boxes.forEach((a, index) => {
    for (const b of layout.boxes.slice(index + 1)) {
      expect({ a: a.id, b: b.id, overlap: overlaps(a, b) }).toEqual({
        a: a.id,
        b: b.id,
        overlap: false,
      });

      if (a.parent === b.parent) {
        expect({ a: a.id, b: b.id, overlap: overlaps(a.extent, b.extent) }).toEqual({
          a: a.id,
          b: b.id,
          overlap: false,
        });
      }
    }
  });

  // Every card inside its frames, and none of another inside them.
  for (const frame of layout.frames) {
    for (const box of layout.boxes) {
      const within = isWithin(tree, box.id, frame.id);

      expect({
        frame: frame.id,
        box: box.id,
        inside: inside(box, frame),
        overlap: overlaps(box, frame),
      }).toEqual({
        frame: frame.id,
        box: box.id,
        inside: within,
        overlap: within,
      });
    }
  }

  // An IF's Then lies left of its Else.
  for (const box of layout.boxes.filter((candidate) => candidate.kind === "if")) {
    const of = (name: string) =>
      layout.boxes.filter(
        (other) =>
          other.id !== box.id &&
          isWithin(tree, other.id, box.id) &&
          tree.byId.get(ancestorIn(tree, other.id, box.id))?.body === name,
      );
    const rightOfThen = Math.max(...of("then").map((other) => other.extent.x + other.extent.w));
    const leftOfElse = Math.min(...of("else").map((other) => other.extent.x));

    expect(rightOfThen <= leftOfElse).toBe(true);

    const ports = layout.ports.filter((port) => port.id === box.id);

    expect(ports).toEqual([
      { id: box.id, branch: "then", x: box.x + PORT_INSET, y: box.y + box.h },
      { id: box.id, branch: "else", x: box.x + box.w - PORT_INSET, y: box.y + box.h },
    ]);
    expect(layout.joins.filter((join) => join.id === box.id)).toEqual([
      { id: box.id, x: box.extent.x + box.extent.w / 2, y: box.extent.y + box.extent.h },
    ]);
  }

  // One arrowhead into every card but the first, its tip 1 px before the card's top, a wire ending at its base. A
  // frame takes the wire in to its header card itself, so the first node has one when it is a group or a repeat.
  const first = layout.boxes.find((box) => box.id === steps[0]?.id);
  const wireEnds = ends(layout);

  for (const box of layout.boxes) {
    const arrows = layout.arrows.filter((arrow) => arrow.to === box.id);
    const framed = box.kind === "group" || box.kind === "repeat";

    expect({ id: box.id, arrows: arrows.length }).toEqual({
      id: box.id,
      arrows: box === first && !framed ? 0 : 1,
    });

    for (const arrow of arrows) {
      expect(arrow).toMatchObject({ x: box.x + box.w / 2, y: box.y - 1 });
      expect(wireEnds.some((end) => same(end, { x: arrow.x, y: arrow.y - ARROW_LENGTH }))).toBe(
        true,
      );
    }
  }

  expectConnected(layout);

  // A slot for every gap of the lists that show, on none of the cards.
  const expected = slotsOf(steps).filter(
    (slot) => slot.parent === null || (!hidden(slot.parent) && !collapsed.has(slot.parent)),
  );

  expect(layout.slots).toHaveLength(expected.length);

  for (const slot of expected) {
    expect(layout.slots.filter((placed) => sameSlot(placed.slot, slot))).toHaveLength(1);
  }

  for (const slot of layout.slots) {
    expect(
      layout.boxes.some((box) => overlaps(box, { x: slot.x - 10, y: slot.y - 10, w: 20, h: 20 })),
    ).toBe(false);
  }

  return layout;
}

// The child of container whose subtree holds id.
function ancestorIn(tree: ReturnType<typeof indexTree>, id: string, container: string): string {
  let current = id;

  for (;;) {
    const parent = tree.byId.get(current)?.parent ?? null;

    if (parent === null || parent === container) {
      return current;
    }

    current = parent;
  }
}

describe("layoutFlow", () => {
  it("stacks a series on one axis with the node sizes of its kinds", () => {
    const layout = expectSound([
      leaf("a"),
      leaf("b"),
      branch("c", [leaf("d")], [leaf("e")]),
      group("f", leaf("g")),
    ]);
    const axis = (id: string) => {
      const box = layout.boxes.find((candidate) => candidate.id === id);

      return box === undefined ? Number.NaN : box.x + box.w / 2;
    };

    expect(new Set(["a", "b", "c", "f", "g"].map(axis)).size).toBe(1);
    expect(layout.width).toBeGreaterThanOrEqual(NODE_WIDTH * 2);
  });

  it("lays out every node of the server's document soundly", () => {
    expectSound(everyNode);
  });

  it("lays out empty lists, branches and bodies with a slot each", () => {
    const layout = expectSound([]);

    expect(layout.slots).toEqual([
      expect.objectContaining({ slot: { parent: null, body: "steps", index: 0 } }),
    ]);
    expect(layout.wires).toEqual([]);

    expectSound([branch("b", [], [])]);
    expectSound([branch("b", [], [leaf("c")]), group("g"), repeat("r")]);
    expectSound([branch("b", [group("g", branch("h", [repeat("r", leaf("x"))]))], [leaf("c")])]);
  });

  it("lays out trees of every shape soundly", () => {
    for (const seed of [1, 2, 3, 4, 5, 6, 7, 8]) {
      expectSound(generated(60, seed));
    }
  });

  it("draws a collapsed container as one card, without what is inside it", () => {
    const steps = [
      leaf("a"),
      branch("b", [leaf("c")], [group("g", leaf("d"))]),
      repeat("r", leaf("e")),
    ];
    const layout = expectSound(steps, new Set(["b", "r"]));

    expect(layout.boxes.map((box) => [box.id, box.kind])).toEqual([
      ["a", "leaf"],
      ["b", "collapsed"],
      ["r", "collapsed"],
    ]);
    expect(layout.frames).toEqual([]);
    expect(layout.ports).toEqual([]);
    expectSound(steps, new Set(["g"]));
  });

  it("gives the same layout for the same tree", () => {
    const steps = generated(120, 3);

    expect(layoutFlow(steps)).toEqual(layoutFlow(structuredClone(steps)));
  });

  it("lays out 300 nodes in under 5 ms", () => {
    const steps = generated(300, 11);

    expect(indexTree(steps).entries).toHaveLength(300);

    for (let warm = 0; warm < 5; warm++) {
      layoutFlow(steps);
    }

    const times: number[] = [];

    for (let run = 0; run < 7; run++) {
      const started = performance.now();
      layoutFlow(steps);
      times.push(performance.now() - started);
    }

    expect(Math.min(...times)).toBeLessThan(5);
  });
});

describe("paths", () => {
  it("draws curves with their control points halfway down, and arrowheads pointing down", () => {
    expect(
      wirePath({
        shape: "bend",
        start: { x: 0, y: 0 },
        end: { x: 100, y: 56 },
        from: null,
        to: null,
        branch: null,
      }),
    ).toBe("M0 0 C0 28 100 28 100 56");
    expect(
      wirePath({
        shape: "line",
        start: { x: 1, y: 2 },
        end: { x: 1, y: 30.26 },
        from: null,
        to: null,
        branch: null,
      }),
    ).toBe("M1 2 L1 30.3");
    expect(arrowPath({ x: 10, y: 99, to: "a", branch: null })).toBe("M10 99 L5 91 L15 91 Z");
    expect(
      wirePath({
        shape: "loop",
        points: [
          { x: 100, y: 200 },
          { x: 20, y: 200 },
          { x: 20, y: 100 },
          { x: 100, y: 100 },
        ],
        radius: 12,
        from: "r",
        to: "r",
        branch: null,
      }),
    ).toBe("M100 200 L32 200 Q20 200 20 188 L20 112 Q20 100 32 100 L100 100");
  });
});
