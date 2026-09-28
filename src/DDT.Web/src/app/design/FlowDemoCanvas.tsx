// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { IconPlus } from "@tabler/icons-react";

import { FlowDots } from "@/ui/FlowDots";
import { FlowFrame } from "@/ui/FlowFrame";
import { FlowNode } from "@/ui/FlowNode";
import { FlowViewport } from "@/ui/FlowViewport";
import { FlowWires } from "@/ui/FlowWires";

import type { DemoFlow } from "./useDemoFlow";

interface FlowDemoCanvasProps {
  flow: DemoFlow;
  run: boolean;
  selected: string | null;
}

// The demo flow on the canvas, with the slots of the editor while it is not a run.
export function FlowDemoCanvas({ flow, run, selected }: FlowDemoCanvasProps) {
  const { byId, tree, layout, stateOf, tone } = flow;

  return (
    <FlowViewport
      label="Flow of the demo sequence"
      contentWidth={layout.width}
      contentHeight={layout.height}
      className="h-[36rem]"
      minimap={[
        ...layout.frames.map((frame) => ({ ...frame, tone: "frame" as const })),
        ...layout.boxes.map((box) => ({
          ...box,
          tone: run && stateOf(box.id) === "notTaken" ? ("muted" as const) : ("node" as const),
        })),
      ]}
    >
      {layout.frames.map((frame) => (
        <FlowFrame
          key={frame.id}
          className="absolute"
          style={{ left: frame.x, top: frame.y, width: frame.w, height: frame.h }}
        />
      ))}
      <FlowWires
        width={layout.width}
        height={layout.height}
        wires={layout.wires}
        arrows={layout.arrows}
        tone={tone}
      />
      {layout.boxes.map((box) => {
        const node = byId.get(box.id);
        const leaves = tree.entries.filter(
          (entry) => entry.number !== null && entry.node.id.startsWith(`${box.id}.`),
        );

        return node === undefined ? null : (
          <FlowNode
            key={box.id}
            kind={node.kind}
            name={node.name}
            number={tree.byId.get(box.id)?.number ?? null}
            detail={run ? (node.runDetail ?? node.detail) : node.detail}
            code={node.code === true && !run}
            state={run ? node.run : "edit"}
            percent={40}
            selected={!run && selected === box.id}
            {...(node.problem === true ? { mark: "problem" as const } : {})}
            branch={run && node.kind === "if" ? "then" : null}
            collapsed={box.kind === "collapsed"}
            strip={leaves.map((entry) => ({
              state: !run ? "waiting" : stateOf(entry.node.id) === "skipped" ? "skipped" : "done",
            }))}
            className="absolute"
            style={{ left: box.x, top: box.y, width: box.w, height: box.h }}
          />
        );
      })}
      <FlowDots
        width={layout.width}
        height={layout.height}
        ports={layout.ports}
        joins={layout.joins}
        selectedId={run ? null : selected}
        tone={tone}
      />
      {run
        ? null
        : layout.slots.map((slot, index) => (
            <span
              key={index}
              aria-hidden="true"
              className="absolute flex size-5 -translate-x-1/2 -translate-y-1/2 items-center justify-center rounded-full bg-raised text-muted shadow-[inset_0_0_0_1px_var(--color-line)]"
              style={{ left: slot.x, top: slot.y }}
            >
              <IconPlus size={12} stroke={2} />
            </span>
          ))}
    </FlowViewport>
  );
}
