// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import type { KeyboardEvent } from "react";
import { useDrag } from "react-aria-components";

import { cx } from "@/ui/cx";
import { FlowNode } from "@/ui/FlowNode";

import { nodeTitle } from "../../flow/flowLabels";
import type { SequenceStep } from "../../sequences";
import { NODE_TYPE, type FlowDrag } from "../flowDrag";
import type { NodeDetail } from "../nodeDetail";
import type { FindingMark, StripItem } from "./nodeMarks";

interface CanvasNodeProps {
  node: SequenceStep;
  box: { x: number; y: number; w: number; h: number; id: string };
  number: number | null;
  label: string;
  detail: NodeDetail;
  mark: FindingMark | undefined;
  collapsed: boolean;
  strip: StripItem[] | undefined;
  selected: boolean;
  tabbable: boolean;
  locked: boolean;
  register: (element: HTMLElement | null) => void;
  onPress: () => void;
  onFocus: () => void;
  onKeyDown: (event: KeyboardEvent) => void;
  onContextMenu: (element: HTMLElement) => void;
  onDragChange: (drag: FlowDrag) => void;
}

// A node's card on the canvas, one stop of the canvas's roving focus.
export function CanvasNode({
  node,
  box,
  number,
  label,
  detail,
  mark,
  collapsed,
  strip,
  selected,
  tabbable,
  locked,
  register,
  onPress,
  onFocus,
  onKeyDown,
  onContextMenu,
  onDragChange,
}: CanvasNodeProps) {
  const { t } = useLingui();
  // Enter opens the node's fields, so the keyboard doesn't drag nodes. It moves a node with Alt and the arrows, cut
  // and paste, or the node's menu. The pointer drags it.
  const { dragProps, isDragging } = useDrag({
    hasDragButton: true,
    isDisabled: locked,
    getItems: () => [{ [NODE_TYPE]: node.id, "text/plain": nodeTitle(node) }],
    getAllowedDropOperations: () => ["move"],
    onDragStart: () => {
      onDragChange({ node: node.id });
    },
    onDragEnd: () => {
      onDragChange(null);
    },
  });

  return (
    <div
      ref={register}
      {...dragProps}
      role="button"
      tabIndex={tabbable ? 0 : -1}
      aria-label={label}
      aria-current={selected ? "true" : undefined}
      aria-keyshortcuts="Enter Delete Shift+F10"
      data-flow-node
      data-node-id={node.id}
      className={cx(
        "absolute cursor-pointer rounded-key outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus",
        isDragging && "opacity-50",
      )}
      style={{ left: box.x, top: box.y, width: box.w, height: box.h }}
      onClick={onPress}
      onFocus={onFocus}
      onKeyDown={onKeyDown}
      onContextMenu={(event) => {
        event.preventDefault();
        onContextMenu(event.currentTarget);
      }}
    >
      <FlowNode
        kind={node.kind}
        name={node.name.trim() === "" ? t`Unnamed step` : node.name}
        number={number}
        detail={detail.text}
        code={detail.code}
        selected={selected}
        {...(mark === undefined ? {} : { mark })}
        collapsed={collapsed}
        {...(strip === undefined ? {} : { strip })}
        className="size-full"
      />
    </div>
  );
}
