// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import { NODE_WIDTH, type WireRoute } from "@/sequences/flow/flowGeometry";
import { FlowDots } from "@/ui/FlowDots";
import { FlowFrame } from "@/ui/FlowFrame";
import { FlowViewport } from "@/ui/FlowViewport";
import { FlowWires, type WireTone } from "@/ui/FlowWires";

import { FollowToggle } from "./FollowToggle";
import { HandoverLine } from "./HandoverLine";
import { nodeDetail } from "./nodeDetail";
import { RunFlowNode } from "./RunFlowNode";
import { handover, minimapItems } from "./runFlowView";
import type { RunFollow } from "./useFollowRun";
import type { NodeFocus } from "./useNodeFocus";
import type { RunFlowModel } from "./useRunFlowModel";

interface RunFlowCanvasProps {
  model: RunFlowModel;
  follow: RunFollow;
  focus: NodeFocus;
  // A run that ended no longer moves, so only one still going and at a node offers "Follow the run".
  canFollow: boolean;
  variables: Record<string, string>;
  now: number;
}

// The run's flow: taken wires in ink, the others dashed, and a line where the run handed over to Windows.
export function RunFlowCanvas({
  model,
  follow,
  focus,
  canFollow,
  variables,
  now,
}: RunFlowCanvasProps) {
  const { t: translate } = useLingui();
  const { layout, index, path, subjects } = model;
  const tone = (route: WireRoute): WireTone => path.tone(route);
  const hand = handover(path);
  const handBox = hand === null ? undefined : layout.boxes.find((box) => box.id === hand);

  return (
    <FlowViewport
      label={translate`Flow of this run`}
      contentWidth={Math.max(layout.width, NODE_WIDTH)}
      contentHeight={layout.height}
      tabbable={false}
      start="top"
      handle={follow.viewport}
      className="h-[32rem] max-sm:h-[26rem]"
      onTransformChange={follow.onTransformChange}
      minimap={minimapItems(layout, path)}
      {...(canFollow
        ? {
            controls: <FollowToggle isSelected={follow.following} onChange={follow.setFollowing} />,
          }
        : {})}
    >
      {layout.frames.map((frame) => (
        <FlowFrame
          key={frame.id}
          className="absolute"
          style={{ left: frame.x, top: frame.y, width: frame.w, height: frame.h }}
        />
      ))}

      {handBox === undefined ? null : <HandoverLine top={handBox.y} width={layout.width} />}

      <FlowWires
        width={layout.width}
        height={layout.height}
        wires={layout.wires}
        arrows={layout.arrows}
        tone={tone}
      />

      {layout.boxes.map((box) => {
        const node = path.byId.get(box.id);
        const entry = index.byId.get(box.id);

        if (node === undefined || entry === undefined) {
          return null;
        }

        return (
          <RunFlowNode
            key={box.id}
            box={box}
            node={node}
            entry={entry}
            index={index}
            detail={nodeDetail(node, subjects, variables, now)}
            isTabStop={box.id === focus.tabStopId}
            isSelected={box.id === focus.selectedId}
            handlers={focus.handlers(box)}
          />
        );
      })}

      <FlowDots
        width={layout.width}
        height={layout.height}
        ports={layout.ports}
        joins={layout.joins}
        selectedId={focus.selectedId}
        tone={tone}
      />
    </FlowViewport>
  );
}
