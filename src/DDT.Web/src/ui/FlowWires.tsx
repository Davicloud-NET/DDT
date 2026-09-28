// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { FlowArrow, FlowWire, WireRoute } from "@/sequences/flow/flowGeometry";
import { arrowPath, wirePath } from "@/sequences/flow/wirePaths";

import { cx } from "./cx";
import { wireFills } from "./wireFills";

// How a wire is drawn. While a sequence is edited, every wire is "edit". On a run's page, the path the run took
// is "taken", in ink. What's still ahead of it is "ahead", and a branch it didn't take is "not", dashed and
// faint.
export type WireTone = "edit" | "taken" | "ahead" | "not";

const strokes: Record<WireTone, string> = {
  edit: "stroke-control",
  ahead: "stroke-control",
  taken: "stroke-ink stroke-[2.25]",
  not: "stroke-line [stroke-dasharray:5_5]",
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
            {heads === "" ? null : <path d={heads} className={wireFills[of]} />}
          </g>
        );
      })}
    </svg>
  );
}
