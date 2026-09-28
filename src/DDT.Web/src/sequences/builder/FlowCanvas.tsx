// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { FlowDots } from "@/ui/FlowDots";
import { FlowViewport } from "@/ui/FlowViewport";

import { NODE_WIDTH, type FlowLayout } from "../flow/flowGeometry";
import type { FlowCommand } from "../flow/flowKeyboard";
import type { Slot, TreeIndex } from "../flow/flowTree";
import type { Findings } from "../problems";
import type { SequencePhase, SequenceStep, StepKind } from "../sequences";
import { CanvasBackdrop } from "./canvas/CanvasBackdrop";
import { CanvasNodes } from "./canvas/CanvasNodes";
import { CanvasSlots } from "./canvas/CanvasSlots";
import type { NodeAction } from "./canvas/nodeAction";
import { NodeMenu } from "./canvas/NodeMenu";
import type { FlowReveal } from "./canvas/useCanvasReveal";
import { useFlowCanvas } from "./canvas/useFlowCanvas";
import type { FlowDrag } from "./flowDrag";
import type { NodeDetail } from "./nodeDetail";
import { NodeKindsMenu } from "./NodeKindsMenu";

interface FlowCanvasProps {
  label: string;
  steps: SequenceStep[];
  index: TreeIndex;
  layout?: FlowLayout;
  selectedId: string | null;
  reveal: FlowReveal | null;
  findings: Findings;
  phases: ReadonlyMap<string, readonly SequencePhase[]>;
  collapsed: ReadonlySet<string>;
  locked: boolean;
  drag: FlowDrag;
  onDragChange: (drag: FlowDrag) => void;
  detailOf: (node: SequenceStep) => NodeDetail;
  onSelect: (id: string) => void;
  onCommand: (command: FlowCommand, id: string) => void;
  onAction: (action: NodeAction, id: string) => void;
  onAdd: (slot: Slot, kind: StepKind) => void;
  onMove: (ids: string[], slot: Slot) => void;
  // A node whose gap after it should open its menu of kinds, as its own menu asks.
  addAfter: string | null;
  onAddAfterDone: () => void;
  className?: string;
}

// The flow as cards on a canvas that pans and zooms: one Tab stop, with a roving focus that follows the flow.
export function FlowCanvas(props: FlowCanvasProps) {
  const { steps, index, selectedId, findings, locked, drag, onSelect, onAdd, className } = props;
  const { layout, menus, ...canvas } = useFlowCanvas(props);
  const { menuSlot, menuNode } = menus;
  const menuEntry = menuNode === null ? undefined : index.byId.get(menuNode);
  const empty = steps.length === 0;

  return (
    <FlowViewport
      label={props.label}
      contentWidth={Math.max(layout.width, NODE_WIDTH)}
      contentHeight={layout.height}
      tabbable={false}
      start="top"
      handle={canvas.viewport}
      {...(className === undefined ? {} : { className })}
      {...(empty
        ? {}
        : {
            minimap: [
              ...layout.frames.map((frame) => ({ ...frame, tone: "frame" as const })),
              ...layout.boxes.map((box) => ({ ...box, tone: "node" as const })),
            ],
          })}
    >
      <CanvasBackdrop layout={layout} steps={steps} phases={props.phases} />

      <CanvasNodes
        boxes={layout.boxes}
        index={index}
        findings={findings}
        selectedId={selectedId}
        tabbableId={canvas.tabbableId}
        locked={locked}
        detailOf={props.detailOf}
        register={canvas.registerNode}
        onSelect={onSelect}
        onReveal={canvas.revealRect}
        onKeyDown={canvas.keyDown}
        onMenu={menus.openNodeMenu}
        onDragChange={props.onDragChange}
      />

      <FlowDots
        width={layout.width}
        height={layout.height}
        ports={layout.ports}
        joins={layout.joins}
        selectedId={selectedId}
      />

      {locked ? null : (
        <CanvasSlots
          slots={layout.slots}
          index={index}
          tabbable={empty}
          drag={drag}
          menuSlot={menuSlot}
          canMove={canvas.canMove}
          register={canvas.registerSlot}
          onOpen={menus.openSlotMenu}
          onReveal={canvas.revealRect}
          onAdd={onAdd}
          onMove={props.onMove}
        />
      )}

      {empty ? <EmptyHint locked={locked} /> : null}

      {menuSlot === null ? null : (
        <NodeKindsMenu
          triggerRef={menus.slotTrigger}
          isOpen
          onOpenChange={(open) => {
            if (!open) {
              menus.closeSlotMenu();
            }
          }}
          onAdd={(kind) => {
            onAdd(menuSlot, kind);
          }}
        />
      )}

      {menuEntry === undefined ? null : (
        <NodeMenu
          node={menuEntry.node}
          triggerRef={menus.nodeTrigger}
          locked={locked}
          collapsed={props.collapsed.has(menuEntry.node.id)}
          onClose={menus.closeNodeMenu}
          onAction={props.onAction}
        />
      )}
    </FlowViewport>
  );
}

function EmptyHint({ locked }: { locked: boolean }) {
  return (
    <p className="absolute top-full left-1/2 w-72 -translate-x-1/2 pt-2 text-center type-small text-muted">
      {locked ? (
        <Trans>This sequence has no steps yet.</Trans>
      ) : (
        <Trans>No steps yet. Drag one here from the palette, or choose + to add one.</Trans>
      )}
    </p>
  );
}
