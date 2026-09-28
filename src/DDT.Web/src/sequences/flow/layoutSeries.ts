// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceStep } from "../sequences";
import {
  arrowInto,
  EMPTY_HEIGHT,
  EMPTY_WIDTH,
  GAP,
  type LayoutContext,
  type Part,
  type Place,
} from "./layoutParts";

// A series stacks its nodes on one axis with a gap between them. bare leaves out the wire of an empty series, as the
// top of the flow has nothing leading into it.
export function seriesPart(
  context: LayoutContext,
  list: readonly SequenceStep[],
  where: Place,
  bare: boolean,
): Part {
  const kids = list.map((node) => context.measure(node, where));

  if (kids.length === 0) {
    return emptyPart(context, where, bare);
  }

  const axis = Math.max(...kids.map((kid) => kid.axis));
  const right = Math.max(...kids.map((kid) => kid.w - kid.axis));
  const height = kids.reduce((sum, kid) => sum + kid.h, 0) + GAP * (kids.length - 1);

  return {
    w: axis + right,
    h: height,
    axis,
    id: kids[0]?.id ?? null,
    framed: kids[0]?.framed ?? false,
    place: (x, y) => {
      let top = y;
      let previous: Part | null = null;

      kids.forEach((kid, index) => {
        if (previous !== null) {
          context.enter({ x: x + axis, y: top - GAP }, { x: x + axis, y: top }, arrowInto(kid), {
            from: previous.id,
            to: kid.id,
            branch: null,
          });
          context.slot(x + axis, top - GAP / 2, where.parent, where.body, index);
        }

        kid.place(x + axis - kid.axis, top);
        top += kid.h + GAP;
        previous = kid;
      });
    },
  };
}

// An empty body or branch: a stretch of wire with its one slot.
function emptyPart(context: LayoutContext, where: Place, bare: boolean): Part {
  return {
    w: EMPTY_WIDTH,
    h: EMPTY_HEIGHT,
    axis: EMPTY_WIDTH / 2,
    id: null,
    framed: false,
    place: (x, y) => {
      const axis = x + EMPTY_WIDTH / 2;

      if (!bare) {
        context.line(
          { x: axis, y },
          { x: axis, y: y + EMPTY_HEIGHT },
          {
            from: null,
            to: null,
            branch: where.branch,
          },
        );
      }

      context.slot(axis, y + EMPTY_HEIGHT / 2, where.parent, where.body, 0);
    },
  };
}
