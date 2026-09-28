// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";
import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { isTextDropItem, useDrag, useDrop, type DropItem } from "react-aria-components";

import { cx } from "@/ui/cx";
import { FlowFrame, FlowNode } from "@/ui/FlowNode";
import { FlowViewport, type FlowViewportHandle } from "@/ui/FlowViewport";
import { FlowDots, FlowWires } from "@/ui/FlowWires";
import { Menu, MenuItem, MenuSeparator } from "@/ui/Menu";

import {
  flowCommand,
  flowTarget,
  nodeLabel,
  nodeTitle,
  slotLabel,
  type FlowCommand,
} from "../flow/flowKeyboard";
import { layoutFlow, NODE_WIDTH, type FlowLayout } from "../flow/flowLayout";
import { isWithin, sameSlot, type Slot, type TreeIndex } from "../flow/flowTree";
import { stepFindings, type Findings } from "../problems";
import type { ContainerKind, SequencePhase, SequenceStep, StepKind } from "../sequences";
import { isContainer, isStepKind } from "../steps";
import { NodeKindsMenu } from "./AddNodeMenu";
import type { NodeDetail } from "./nodeDetail";

// What the flow carries while something is dragged over it: a kind from the palette, or a node of the flow.
export const KIND_TYPE = "application/x-ddt-flow-kind";
export const NODE_TYPE = "application/x-ddt-flow-node";

export type FlowDrag = { kind: StepKind } | { node: string } | null;

// Asks the canvas to show a node, and to give it the focus unless the page takes that elsewhere.
export interface FlowReveal {
  id: string;
  focus: boolean;
  center: boolean;
  // Counts up, so asking for the same node twice shows it twice.
  count: number;
}

// The things a node's menu does, beside the keys.
export type NodeAction =
  | { type: "wrap"; kind: ContainerKind }
  | { type: "unwrap" }
  | { type: "collapse" }
  | { type: "addAfter" }
  | { type: "command"; command: FlowCommand };

// Whether moving the node to the slot changes anything and keeps the tree a tree.
function canMoveTo(index: TreeIndex, id: string, slot: Slot): boolean {
  const entry = index.byId.get(id);

  if (entry === undefined || (slot.parent !== null && isWithin(index, slot.parent, id))) {
    return false;
  }

  return !(
    slot.parent === entry.parent &&
    slot.body === entry.body &&
    (slot.index === entry.index || slot.index === entry.index + 1)
  );
}

async function droppedText(items: readonly DropItem[], type: string): Promise<string | null> {
  for (const item of items) {
    if (isTextDropItem(item) && item.types.has(type)) {
      return item.getText(type);
    }
  }

  return null;
}

// The top list's first node that runs only after the hand-over, where the flow draws the line between Windows PE and
// the installed Windows; null where no such line runs across the whole flow.
function handover(
  steps: readonly SequenceStep[],
  phases: ReadonlyMap<string, readonly SequencePhase[]>,
): string | null {
  const index = steps.findIndex((step) => {
    const own = phases.get(step.id) ?? [];

    return own.length > 0 && !own.includes("WindowsPE");
  });
  const before = steps.slice(0, Math.max(0, index));

  return index > 0 &&
    before.every((step) => (phases.get(step.id) ?? []).every((phase) => phase === "WindowsPE"))
    ? (steps[index]?.id ?? null)
    : null;
}

export function FlowCanvas({
  label,
  steps,
  index,
  layout: given,
  selectedId,
  reveal,
  findings,
  phases,
  collapsed,
  locked,
  drag,
  onDragChange,
  detailOf,
  onSelect,
  onCommand,
  onAction,
  onAdd,
  onMove,
  addAfter,
  onAddAfterDone,
  className,
}: {
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
}) {
  const { t } = useLingui();
  const layout = given ?? layoutFlow(steps, { collapsed });
  const viewport = useRef<FlowViewportHandle>(null);
  const nodes = useRef(new Map<string, HTMLElement>());
  const slots = useRef(new Map<string, HTMLButtonElement>());
  const [menuSlot, setMenuSlot] = useState<Slot | null>(null);
  const slotTrigger = useRef<HTMLElement | null>(null);
  const [menuNode, setMenuNode] = useState<string | null>(null);
  const nodeTrigger = useRef<HTMLElement | null>(null);
  // The node being dragged, read while the drag goes on.
  const dragged = useRef<string | null>(null);

  useEffect(() => {
    dragged.current = drag !== null && "node" in drag ? drag.node : null;
  });

  const boxOf = (id: string) => layout.boxes.find((box) => box.id === id);
  const tabbableId =
    selectedId !== null && index.byId.has(selectedId) && boxOf(selectedId) !== undefined
      ? selectedId
      : (steps[0]?.id ?? null);
  const slotKey = (slot: Slot) => `${slot.parent ?? ""}/${slot.body}/${String(slot.index)}`;

  useEffect(() => {
    if (reveal === null) {
      return;
    }

    const box = layout.boxes.find((candidate) => candidate.id === reveal.id);

    if (box !== undefined) {
      viewport.current?.reveal(box, reveal.center);
    }

    if (!reveal.focus) {
      return;
    }

    // A menu that closes keeps the rest of the page out of reach until it is gone, so the focus is given again on the
    // next frames until the node has it.
    let frame = 0;
    let tries = 0;
    const give = () => {
      const element = nodes.current.get(reveal.id);

      element?.focus({ preventScroll: true });

      if (element !== undefined && document.activeElement !== element && tries++ < 10) {
        frame = requestAnimationFrame(give);
      }
    };

    frame = requestAnimationFrame(give);

    return () => {
      cancelAnimationFrame(frame);
    };
    // Only a new request moves the view; a new layout alone does not.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reveal]);

  useEffect(() => {
    if (addAfter === null) {
      return;
    }

    const entry = index.byId.get(addAfter);

    onAddAfterDone();

    if (entry === undefined) {
      return;
    }

    const slot = { parent: entry.parent, body: entry.body, index: entry.index + 1 };
    const element = slots.current.get(slotKey(slot));

    if (element !== undefined) {
      slotTrigger.current = element;
      setMenuSlot(slot);
    }
  }, [addAfter, index, onAddAfterDone]);

  const keyDown = (event: KeyboardEvent, id: string) => {
    const command = flowCommand(event);

    if (command === null) {
      return;
    }

    if (command.type === "move") {
      const target = flowTarget(index, id, command.move, collapsed);

      // Escape at the top leaves the key to the page.
      if (target === null && command.move === "parent") {
        return;
      }

      event.preventDefault();

      if (target !== null) {
        onSelect(target);
        nodes.current.get(target)?.focus({ preventScroll: true });
      }

      return;
    }

    if (command.type === "menu") {
      event.preventDefault();
      nodeTrigger.current = nodes.current.get(id) ?? null;
      setMenuNode(id);
      return;
    }

    if (locked && command.type !== "open" && command.type !== "copy") {
      return;
    }

    event.preventDefault();
    onCommand(command, id);
  };

  const hand = handover(steps, phases);
  const handBox = hand === null ? undefined : boxOf(hand);
  const menuEntry = menuNode === null ? undefined : index.byId.get(menuNode);
  const menuName = menuEntry === undefined ? "" : nodeTitle(menuEntry.node);
  const empty = steps.length === 0;

  return (
    <FlowViewport
      label={label}
      contentWidth={Math.max(layout.width, NODE_WIDTH)}
      contentHeight={layout.height}
      tabbable={false}
      start="top"
      handle={viewport}
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
      {layout.frames.map((frame) => (
        <FlowFrame
          key={frame.id}
          className="absolute"
          style={{ left: frame.x, top: frame.y, width: frame.w, height: frame.h }}
        />
      ))}

      {handBox === undefined ? null : (
        <div aria-hidden="true">
          <div
            className="absolute border-t-[1.5px] border-dashed border-control"
            style={{ left: -48, width: layout.width + 96, top: handBox.y - 22 }}
          />
          <span
            className="absolute type-small whitespace-nowrap text-muted"
            style={{ left: -44, top: handBox.y - 44 }}
          >
            <Trans>In Windows PE</Trans>
          </span>
          <span
            className="absolute type-small whitespace-nowrap text-muted"
            style={{ left: -44, top: handBox.y - 16 }}
          >
            <Trans>In the installed Windows, after the hand-over</Trans>
          </span>
        </div>
      )}

      <FlowWires
        width={layout.width}
        height={layout.height}
        wires={layout.wires}
        arrows={layout.arrows}
      />

      {layout.boxes.map((box) => {
        const entry = index.byId.get(box.id);

        if (entry === undefined) {
          return null;
        }

        const own = stepFindings(findings, box.id);

        return (
          <CanvasNode
            key={box.id}
            node={entry.node}
            box={box}
            number={entry.number}
            label={nodeLabel(index, box.id, own.problems.length, own.warnings.length)}
            detail={detailOf(entry.node)}
            mark={
              own.problems.length > 0 ? "problem" : own.warnings.length > 0 ? "warning" : undefined
            }
            collapsed={box.kind === "collapsed"}
            strip={
              box.kind === "collapsed"
                ? index.entries
                    .filter(
                      (inner) =>
                        inner.number !== null &&
                        inner.node.id !== box.id &&
                        isWithin(index, inner.node.id, box.id),
                    )
                    .map((inner) => {
                      const findingsOf = stepFindings(findings, inner.node.id);

                      return {
                        state: "waiting" as const,
                        ...(findingsOf.problems.length > 0
                          ? { mark: "problem" as const }
                          : findingsOf.warnings.length > 0
                            ? { mark: "warning" as const }
                            : {}),
                      };
                    })
                : undefined
            }
            selected={box.id === selectedId}
            tabbable={box.id === tabbableId}
            locked={locked}
            register={(element) => {
              if (element === null) {
                nodes.current.delete(box.id);
              } else {
                nodes.current.set(box.id, element);
              }
            }}
            onPress={() => {
              onSelect(box.id);
            }}
            onFocus={() => {
              viewport.current?.reveal(box);
            }}
            onKeyDown={(event) => {
              keyDown(event, box.id);
            }}
            onContextMenu={(element) => {
              onSelect(box.id);
              nodeTrigger.current = element;
              setMenuNode(box.id);
            }}
            onDragChange={onDragChange}
          />
        );
      })}

      <FlowDots
        width={layout.width}
        height={layout.height}
        ports={layout.ports}
        joins={layout.joins}
        selectedId={selectedId}
      />

      {locked
        ? null
        : layout.slots.map((placed) => {
            const key = slotKey(placed.slot);

            return (
              <SlotKey
                key={key}
                slot={placed.slot}
                x={placed.x}
                y={placed.y}
                label={slotLabel(index, placed.slot)}
                tabbable={empty}
                drag={drag}
                open={menuSlot !== null && sameSlot(menuSlot, placed.slot)}
                canMove={(slot) =>
                  dragged.current !== null && canMoveTo(index, dragged.current, slot)
                }
                register={(element) => {
                  if (element === null) {
                    slots.current.delete(key);
                  } else {
                    slots.current.set(key, element);
                  }
                }}
                onOpen={(element) => {
                  slotTrigger.current = element;
                  setMenuSlot(placed.slot);
                }}
                onFocus={() => {
                  viewport.current?.reveal({ x: placed.x - 24, y: placed.y - 24, w: 48, h: 48 });
                }}
                onAdd={onAdd}
                onMove={onMove}
              />
            );
          })}

      {empty ? (
        <p className="absolute top-full left-1/2 w-72 -translate-x-1/2 pt-2 text-center type-small text-muted">
          {locked ? (
            <Trans>This sequence has no steps yet.</Trans>
          ) : (
            <Trans>No steps yet. Drag one here from the palette, or choose + to add one.</Trans>
          )}
        </p>
      ) : null}

      {menuSlot === null ? null : (
        <NodeKindsMenu
          triggerRef={slotTrigger}
          isOpen
          onOpenChange={(open) => {
            if (!open) {
              setMenuSlot(null);
            }
          }}
          onAdd={(kind) => {
            onAdd(menuSlot, kind);
          }}
        />
      )}

      {menuEntry === undefined ? null : (
        <Menu
          aria-label={t`Actions for ${menuName}`}
          triggerRef={nodeTrigger}
          isOpen
          placement="bottom start"
          onOpenChange={(open) => {
            if (!open) {
              setMenuNode(null);
            }
          }}
          onAction={(key) => {
            const id = menuEntry.node.id;

            setMenuNode(null);

            switch (String(key)) {
              case "wrapGroup":
                onAction({ type: "wrap", kind: "group" }, id);
                break;
              case "wrapIf":
                onAction({ type: "wrap", kind: "if" }, id);
                break;
              case "wrapRepeat":
                onAction({ type: "wrap", kind: "repeat" }, id);
                break;
              case "unwrap":
                onAction({ type: "unwrap" }, id);
                break;
              case "collapse":
                onAction({ type: "collapse" }, id);
                break;
              case "addAfter":
                onAction({ type: "addAfter" }, id);
                break;
              case "duplicate":
                onAction({ type: "command", command: { type: "duplicate" } }, id);
                break;
              case "copy":
                onAction({ type: "command", command: { type: "copy" } }, id);
                break;
              case "cut":
                onAction({ type: "command", command: { type: "cut" } }, id);
                break;
              case "remove":
                onAction({ type: "command", command: { type: "remove" } }, id);
                break;
            }
          }}
        >
          {locked ? null : (
            <>
              <MenuItem id="addAfter">
                <Trans>Add a step after it</Trans>
              </MenuItem>
              <MenuSeparator />
              <MenuItem id="wrapGroup">
                <Trans>Wrap in a group</Trans>
              </MenuItem>
              <MenuItem id="wrapIf">
                <Trans>Wrap in an If</Trans>
              </MenuItem>
              <MenuItem id="wrapRepeat">
                <Trans>Wrap in a Repeat</Trans>
              </MenuItem>
              {isContainer(menuEntry.node) ? (
                <MenuItem id="unwrap">
                  <Trans>Unwrap</Trans>
                </MenuItem>
              ) : null}
            </>
          )}
          {isContainer(menuEntry.node) ? (
            <MenuItem id="collapse">
              {collapsed.has(menuEntry.node.id) ? <Trans>Expand</Trans> : <Trans>Collapse</Trans>}
            </MenuItem>
          ) : null}
          <MenuSeparator />
          <MenuItem id="copy">
            <Trans>Copy</Trans>
          </MenuItem>
          {locked ? null : (
            <>
              <MenuItem id="cut">
                <Trans>Cut</Trans>
              </MenuItem>
              <MenuItem id="duplicate">
                <Trans>Duplicate</Trans>
              </MenuItem>
              <MenuItem id="remove" className="text-fail-text">
                <Trans>Remove</Trans>
              </MenuItem>
            </>
          )}
        </Menu>
      )}
    </FlowViewport>
  );
}

function CanvasNode({
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
}: {
  node: SequenceStep;
  box: { x: number; y: number; w: number; h: number; id: string };
  number: number | null;
  label: string;
  detail: NodeDetail;
  mark: "problem" | "warning" | undefined;
  collapsed: boolean;
  strip: { state: "waiting"; mark?: "problem" | "warning" }[] | undefined;
  selected: boolean;
  tabbable: boolean;
  locked: boolean;
  register: (element: HTMLElement | null) => void;
  onPress: () => void;
  onFocus: () => void;
  onKeyDown: (event: KeyboardEvent) => void;
  onContextMenu: (element: HTMLElement) => void;
  onDragChange: (drag: FlowDrag) => void;
}) {
  const { t } = useLingui();
  // Enter opens the node's fields, so the keyboard moves a node with Alt and the arrows, cut and paste, or its menu;
  // the pointer drags it.
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

function SlotKey({
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
}: {
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
}) {
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
