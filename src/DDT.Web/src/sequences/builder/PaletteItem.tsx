// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { IconGripVertical } from "@tabler/icons-react";
import type { MouseEvent } from "react";
import { useDrag } from "react-aria-components";

import { cx } from "@/ui/cx";
import { NodeGlyph } from "@/ui/NodeGlyph";

import type { StepKind } from "../sequences";
import { KIND_TYPE, type FlowDrag } from "./flowDrag";
import { addLabel } from "./nodeKinds";

interface PaletteItemProps {
  kind: StepKind;
  onAdd: (kind: StepKind) => void;
  onDragChange: (drag: FlowDrag) => void;
}

export function PaletteItem({ kind, onAdd, onDragChange }: PaletteItemProps) {
  const label = addLabel(kind);
  const { dragProps, isDragging } = useDrag({
    getItems: () => [{ [KIND_TYPE]: kind, "text/plain": label }],
    getAllowedDropOperations: () => ["copy"],
    onDragStart: () => {
      onDragChange({ kind });
    },
    onDragEnd: () => {
      onDragChange(null);
    },
  });

  return (
    <button
      type="button"
      {...dragProps}
      className={cx(
        "flex h-8.5 shrink-0 cursor-grab items-center gap-2.5 rounded-key px-2 text-left type-body text-ink motion-colors outline-none",
        "hover:bg-hover focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus",
        isDragging && "bg-selected",
      )}
      onClick={(event: MouseEvent<HTMLButtonElement>) => {
        dragProps.onClick?.(event);

        // A screen reader's click starts a drag; a pointer's adds the kind.
        if (!event.defaultPrevented) {
          onAdd(kind);
        }
      }}
    >
      <NodeGlyph kind={kind} className="text-ink-2" />
      <span className="flex-1 truncate">{label}</span>
      <IconGripVertical aria-hidden="true" size={14} stroke={2} className="shrink-0 text-control" />
    </button>
  );
}
