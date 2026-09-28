// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import type { FlowBox } from "@/sequences/flow/flowGeometry";
import { nodeTitle, placeLabel } from "@/sequences/flow/flowLabels";
import type { TreeEntry, TreeIndex } from "@/sequences/flow/flowTree";
import { FlowNode } from "@/ui/FlowNode";

import type { PathNode } from "../runPath";
import { pathStateLabel } from "../runView";
import type { NodeDetail } from "./nodeDetail";
import { branchOf, flowState } from "./runFlowView";
import type { NodeHandlers } from "./useNodeFocus";

interface RunFlowNodeProps {
  box: FlowBox;
  node: PathNode;
  entry: TreeEntry;
  index: TreeIndex;
  detail: NodeDetail;
  isTabStop: boolean;
  isSelected: boolean;
  handlers: NodeHandlers;
}

// A node of the run's flow as a button, with its state along the card's top edge.
export function RunFlowNode({
  box,
  node,
  entry,
  index,
  detail,
  isTabStop,
  isSelected,
  handlers,
}: RunFlowNodeProps) {
  const { i18n } = useLingui();
  const state = i18n._(pathStateLabel[node.state]);

  return (
    <div
      {...handlers}
      role="button"
      tabIndex={isTabStop ? 0 : -1}
      aria-label={`${placeLabel(index, entry)}, ${nodeTitle(node.node)}, ${state}`}
      aria-current={isSelected ? "true" : undefined}
      data-flow-node
      className="absolute cursor-pointer rounded-key outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
      style={{ left: box.x, top: box.y, width: box.w, height: box.h }}
    >
      <FlowNode
        kind={node.node.kind}
        name={node.step?.name ?? node.node.name}
        number={entry.number}
        detail={detail.text}
        code={detail.code}
        state={flowState[node.state]}
        {...(node.state === "running" ? { percent: node.step?.percent ?? 0 } : {})}
        selected={isSelected}
        branch={branchOf(node)}
        className="size-full"
      />
    </div>
  );
}
