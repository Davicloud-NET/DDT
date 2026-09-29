// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { FlowFrame } from "@/ui/FlowFrame";
import { FlowWires } from "@/ui/FlowWires";

import type { FlowLayout } from "../../flow/flowGeometry";
import type { SequencePhase, SequenceStep } from "../../sequences";
import { handover } from "./handover";

interface CanvasBackdropProps {
  layout: FlowLayout;
  steps: readonly SequenceStep[];
  phases: ReadonlyMap<string, readonly SequencePhase[]>;
}

// What the canvas draws under the cards: the containers' frames, the hand-over line and the wires.
export function CanvasBackdrop({ layout, steps, phases }: CanvasBackdropProps) {
  const hand = handover(steps, phases);
  const handBox = hand === null ? undefined : layout.boxes.find((box) => box.id === hand);

  return (
    <>
      {layout.frames.map((frame) => (
        <FlowFrame
          key={frame.id}
          className="absolute"
          style={{ left: frame.x, top: frame.y, width: frame.w, height: frame.h }}
        />
      ))}

      {handBox === undefined ? null : <HandoverLine width={layout.width} y={handBox.y} />}

      <FlowWires
        width={layout.width}
        height={layout.height}
        wires={layout.wires}
        arrows={layout.arrows}
      />
    </>
  );
}

function HandoverLine({ width, y }: { width: number; y: number }) {
  return (
    <div aria-hidden="true">
      <div
        className="absolute border-t-[1.5px] border-dashed border-control"
        style={{ left: -48, width: width + 96, top: y - 22 }}
      />
      <span
        className="absolute type-small whitespace-nowrap text-muted"
        style={{ left: -44, top: y - 44 }}
      >
        <Trans>In Windows PE</Trans>
      </span>
      <span
        className="absolute type-small whitespace-nowrap text-muted"
        style={{ left: -44, top: y - 16 }}
      >
        <Trans>In the installed Windows, after the hand-over</Trans>
      </span>
    </div>
  );
}
