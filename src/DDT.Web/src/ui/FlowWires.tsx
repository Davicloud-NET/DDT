// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import {
  arrowPath,
  wirePath,
  type FlowArrow,
  type FlowJoin,
  type FlowPort,
  type FlowWire,
  type WireRoute,
} from "@/sequences/flow/flowLayout";

import { cx } from "./cx";

// How a wire is drawn. While a sequence is edited every wire is "edit". On a run's page the path the run took is
// "taken", in ink; what is still ahead of it is "ahead"; a branch it did not take is "not", dashed and faint.
export type WireTone = "edit" | "taken" | "ahead" | "not";

const strokes: Record<WireTone, string> = {
  edit: "stroke-control",
  ahead: "stroke-control",
  taken: "stroke-ink stroke-[2.25]",
  not: "stroke-line [stroke-dasharray:5_5]",
};

const fills: Record<WireTone, string> = {
  edit: "fill-control",
  ahead: "fill-control",
  taken: "fill-ink",
  not: "fill-line",
};

// A repeat's wire back is dashed until a run went round it.
const loops: Record<WireTone, string> = {
  edit: "stroke-control [stroke-dasharray:4_4]",
  ahead: "stroke-control [stroke-dasharray:4_4]",
  taken: "stroke-ink",
  not: "stroke-line [stroke-dasharray:4_4]",
};

const tones: readonly WireTone[] = ["not", "ahead", "edit", "taken"];

// The wires and arrowheads of a flow's layout as one SVG under the cards, a path per tone, the taken path on top.
export function FlowWires({
  width,
  height,
  wires,
  arrows,
  tone = () => "edit",
  className,
}: {
  width: number;
  height: number;
  wires: readonly FlowWire[];
  arrows: readonly FlowArrow[];
  tone?: (route: WireRoute) => WireTone;
  className?: string;
}) {
  const paths = (list: readonly FlowWire[], of: WireTone) =>
    list
      .filter((wire) => tone(wire) === of)
      .map(wirePath)
      .join(" ");
  const lines = wires.filter((wire) => wire.shape !== "loop");
  const backs = wires.filter((wire) => wire.shape === "loop");

  return (
    <svg
      aria-hidden="true"
      width={width}
      height={height}
      className={cx("pointer-events-none absolute top-0 left-0 overflow-visible", className)}
    >
      {tones.map((of) => {
        const line = paths(lines, of);
        const back = paths(backs, of);
        const heads = arrows
          .filter((arrow) => tone({ from: null, to: arrow.to, branch: arrow.branch }) === of)
          .map(arrowPath)
          .join(" ");

        return (
          <g key={of}>
            {back === "" ? null : (
              <path
                d={back}
                className={cx("fill-none stroke-[1.5]", loops[of])}
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            )}
            {line === "" ? null : (
              <path
                d={line}
                className={cx("fill-none stroke-[1.5]", strokes[of])}
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            )}
            {heads === "" ? null : <path d={heads} className={fills[of]} />}
          </g>
        );
      })}
    </svg>
  );
}

// The dots of a flow: an IF's Then and Else ports on the bottom edge of its card, and where its branches meet. They
// sit over the cards, so a port shows on its card's edge, ringed in the card's colour.
export function FlowDots({
  width,
  height,
  ports,
  joins,
  selectedId = null,
  tone = () => "edit",
  className,
}: {
  width: number;
  height: number;
  ports: readonly FlowPort[];
  joins: readonly FlowJoin[];
  selectedId?: string | null;
  tone?: (route: WireRoute) => WireTone;
  className?: string;
}) {
  return (
    <svg
      aria-hidden="true"
      width={width}
      height={height}
      className={cx("pointer-events-none absolute top-0 left-0 overflow-visible", className)}
    >
      {ports.map((port) => (
        <circle
          key={`${port.id}-${port.branch}`}
          cx={port.x}
          cy={port.y}
          r={4.5}
          strokeWidth={2}
          className={cx(
            fills[tone({ from: port.id, to: null, branch: { id: port.id, name: port.branch } })],
            port.id === selectedId ? "stroke-selected" : "stroke-raised",
          )}
        />
      ))}
      {joins.map((join) => (
        <circle
          key={join.id}
          cx={join.x}
          cy={join.y}
          r={5}
          strokeWidth={2}
          className={cx(fills[tone({ from: join.id, to: null, branch: null })], "stroke-well")}
        />
      ))}
    </svg>
  );
}
