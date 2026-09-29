// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ViewRect } from "@/ui/viewTransform";

import type { FlowSlot } from "../../flow/flowGeometry";
import { slotLabel } from "../../flow/flowLabels";
import { sameSlot, type Slot, type TreeIndex } from "../../flow/flowTree";
import type { StepKind } from "../../sequences";
import type { FlowDrag } from "../flowDrag";
import { SlotKey } from "./SlotKey";
import { slotId } from "./slotId";

interface CanvasSlotsProps {
  slots: readonly FlowSlot[];
  index: TreeIndex;
  tabbable: boolean;
  drag: FlowDrag;
  menuSlot: Slot | null;
  canMove: (slot: Slot) => boolean;
  register: (key: string) => (element: HTMLButtonElement | null) => void;
  onOpen: (slot: Slot, element: HTMLElement) => void;
  onReveal: (rect: ViewRect) => void;
  onAdd: (slot: Slot, kind: StepKind) => void;
  onMove: (ids: string[], slot: Slot) => void;
}

// The canvas's drop slots, one "+" on each gap of the wires.
export function CanvasSlots({
  slots,
  index,
  tabbable,
  drag,
  menuSlot,
  canMove,
  register,
  onOpen,
  onReveal,
  onAdd,
  onMove,
}: CanvasSlotsProps) {
  return slots.map((placed) => {
    const key = slotId(placed.slot);

    return (
      <SlotKey
        key={key}
        slot={placed.slot}
        x={placed.x}
        y={placed.y}
        label={slotLabel(index, placed.slot)}
        tabbable={tabbable}
        drag={drag}
        open={menuSlot !== null && sameSlot(menuSlot, placed.slot)}
        canMove={canMove}
        register={register(key)}
        onOpen={(element) => {
          onOpen(placed.slot, element);
        }}
        onFocus={() => {
          onReveal({ x: placed.x - 24, y: placed.y - 24, w: 48, h: 48 });
        }}
        onAdd={onAdd}
        onMove={onMove}
      />
    );
  });
}
