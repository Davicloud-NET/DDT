// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";
import { useRef } from "react";
import { useDrop } from "react-aria-components";

import { cx } from "@/ui/cx";

import type { Slot } from "../../flow/flowTree";
import type { StepKind } from "../../sequences";
import { isStepKind } from "../../steps";
import { droppedText, KIND_TYPE, NODE_TYPE, type FlowDrag } from "../flowDrag";

interface SlotKeyProps {
  slot: Slot;
  x: number;
  y: number;
  label: string;
  tabbable: boolean;
  drag: FlowDrag;
  open: boolean;
  canMove: (slot: Slot) => boolean;
  register: (element: HTMLButtonElement | null) => void;
  onOpen: (element: HTMLButtonElement) => void;
  onFocus: () => void;
  onAdd: (slot: Slot, kind: StepKind) => void;
  onMove: (ids: string[], slot: Slot) => void;
}

// The "+" in a gap of a wire. It opens the kinds to add, and accepts a kind or a node dropped on it.
export function SlotKey({
  slot,
  x,
  y,
  label,
  tabbable,
  drag,
  open,
  canMove,
  register,
  onOpen,
  onFocus,
  onAdd,
  onMove,
}: SlotKeyProps) {
  const { t } = useLingui();
  const ref = useRef<HTMLButtonElement | null>(null);
  const { dropProps, isDropTarget } = useDrop({
    ref,
    getDropOperation: (types, allowed) =>
      types.has(KIND_TYPE) && allowed.includes("copy")
        ? "copy"
        : types.has(NODE_TYPE) && allowed.includes("move") && canMove(slot)
          ? "move"
          : "cancel",
    onDrop: (event) => {
      void (async () => {
        const kind = await droppedText(event.items, KIND_TYPE);

        if (kind !== null && isStepKind(kind)) {
          onAdd(slot, kind);
          return;
        }

        const id = await droppedText(event.items, NODE_TYPE);

        if (id !== null) {
          onMove([id], slot);
        }
      })();
    },
  });
  const moving = drag !== null && "node" in drag;
  const offered = drag !== null && (!moving || canMove(slot));

  return (
    <button
      ref={(element) => {
        ref.current = element;
        register(element);
      }}
      type="button"
      {...dropProps}
      aria-label={label}
      aria-haspopup="menu"
      aria-expanded={open}
      tabIndex={tabbable ? 0 : -1}
      data-no-pan
      className={cx(
        "group absolute flex -translate-x-1/2 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full outline-none",
        offered ? "size-12" : "size-9",
        drag !== null && !offered && "pointer-events-none opacity-0",
      )}
      style={{ left: x, top: y }}
      onClick={(event) => {
        onOpen(event.currentTarget);
      }}
      onFocus={onFocus}
    >
      <span
        className={cx(
          "flex items-center justify-center rounded-full motion-colors",
          "group-focus-visible:outline-2 group-focus-visible:outline-offset-2 group-focus-visible:outline-focus",
          isDropTarget || open
            ? "size-7 bg-key-primary text-on-key-primary"
            : offered
              ? "size-6 bg-raised text-ink shadow-[inset_0_0_0_1.5px_var(--color-control)]"
              : "size-5 bg-raised text-muted shadow-[inset_0_0_0_1px_var(--color-line)] group-hover:text-ink group-hover:shadow-[inset_0_0_0_1px_var(--color-control)]",
        )}
      >
        <IconPlus aria-hidden="true" size={12} stroke={2} />
      </span>
      {isDropTarget ? (
        <span
          aria-hidden="true"
          className="pointer-events-none absolute top-1/2 left-full ml-1 -translate-y-1/2 rounded-key bg-key-primary px-2.5 py-1.5 type-small whitespace-nowrap text-on-key-primary shadow-overlay"
        >
          {moving ? t`Move here` : t`Add a step here`}
        </span>
      ) : null}
    </button>
  );
}
