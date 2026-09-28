// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMemo } from "react";

import type { WireRoute } from "@/sequences/flow/flowGeometry";
import { layoutFlow } from "@/sequences/flow/flowLayout";
import { indexTree } from "@/sequences/flow/flowTree";
import type { FlowNodeState } from "@/ui/FlowNode";
import type { WireTone } from "@/ui/FlowWires";

import { demoFlow, demoSequence, type DemoNode } from "./demoFlow";

export type DemoFlow = ReturnType<typeof useDemoFlow>;

// The demo flow laid out, edited or as a run, with its group collapsed or not.
export function useDemoFlow(run: boolean, collapsed: boolean) {
  const { steps, byId } = useMemo(() => {
    const nodes = new Map<string, DemoNode>();

    return { steps: demoSequence(demoFlow, "", nodes), byId: nodes };
  }, []);
  const tree = indexTree(steps);
  const layout = layoutFlow(steps, { collapsed: new Set(collapsed ? ["4"] : []) });
  const stateOf = (id: string | null): FlowNodeState =>
    id === null ? "done" : (byId.get(id)?.run ?? "waiting");
  const tone = (route: WireRoute): WireTone => {
    if (!run) {
      return "edit";
    }

    if (route.branch?.name === "else") {
      return "not";
    }

    const state = stateOf(route.to ?? route.from);

    return state === "notTaken" ? "not" : state === "waiting" ? "ahead" : "taken";
  };

  return { byId, tree, layout, stateOf, tone };
}
