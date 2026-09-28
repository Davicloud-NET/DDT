// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useRef } from "react";

import type { Slot, TreeIndex } from "../../flow/flowTree";
import { canMoveTo, type FlowDrag } from "../flowDrag";

// Whether the dragged node may go to a slot. The slots ask during the drag.
export function useDraggedNode(drag: FlowDrag, index: TreeIndex): (slot: Slot) => boolean {
  const dragged = useRef<string | null>(null);

  useEffect(() => {
    dragged.current = drag !== null && "node" in drag ? drag.node : null;
  });

  return (slot) => dragged.current !== null && canMoveTo(index, dragged.current, slot);
}
