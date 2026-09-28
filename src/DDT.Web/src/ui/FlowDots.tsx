// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { FlowJoin, FlowPort, WireRoute } from "@/sequences/flow/flowGeometry";

import { cx } from "./cx";
import type { WireTone } from "./FlowWires";
import { wireFills } from "./wireFills";

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
            wireFills[
              tone({ from: port.id, to: null, branch: { id: port.id, name: port.branch } })
            ],
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
          className={cx(wireFills[tone({ from: join.id, to: null, branch: null })], "stroke-well")}
        />
      ))}
    </svg>
  );
}
