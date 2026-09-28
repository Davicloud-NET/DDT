// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconChevronRight } from "@tabler/icons-react";
import { useState } from "react";
import {
  Button as AriaButton,
  Collection,
  DropIndicator,
  Tree,
  TreeItem,
  TreeItemContent,
  useDragAndDrop,
  type Key,
} from "react-aria-components";

import { cx } from "@/ui/cx";
import { NodeGlyph } from "@/ui/FlowNode";
import { StateTag } from "@/ui/StateTag";

import { nodeLabel, nodeTitle } from "../flow/flowKeyboard";
import { isWithin, type BodyName, type Slot, type TreeIndex } from "../flow/flowTree";
import { stepFindings, type Findings } from "../problems";
import type { SequenceStep } from "../sequences";
import { findingCounts } from "../sequenceList";
import { isContainer } from "../steps";
import { NODE_TYPE } from "./FlowCanvas";

// A row of the outline: a node, or the Then or the Else of an IF, which hold its branches. path numbers each row by
// where it is, such as 2.1.3 for the third node of the Then of the second node.
interface OutlineRow {
  id: string;
  kind: "node" | "branch";
  node: SequenceStep;
  branch: "then" | "else" | null;
  path: string;
  children: OutlineRow[];
}

const branchSeparator = "|";

function rowsOf(list: readonly SequenceStep[], prefix: string): OutlineRow[] {
  return list.map((node, index) => {
    const path = `${prefix}${String(index + 1)}`;
    const children: OutlineRow[] =
      node.kind === "if"
        ? (["then", "else"] as const).map((branch, position) => ({
            id: `${node.id}${branchSeparator}${branch}`,
            kind: "branch",
            node,
            branch,
            path: `${path}.${String(position + 1)}`,
            children: rowsOf(node[branch], `${path}.${String(position + 1)}.`),
          }))
        : node.kind === "group" || node.kind === "repeat"
          ? rowsOf(node.steps, `${path}.`)
          : [];

    return { id: node.id, kind: "node", node, branch: null, path, children };
  });
}

// The container and body a row holds its nodes in: a container's body, or the branch of a Then or an Else row.
function bodyOf(row: { key: string; index: TreeIndex }): { parent: string; body: BodyName } | null {
  const [id = "", branch] = row.key.split(branchSeparator);
  const node = row.index.byId.get(id)?.node;

  if (node === undefined) {
    return null;
  }

  if (branch === "then" || branch === "else") {
    return { parent: id, body: branch };
  }

  return node.kind === "group" || node.kind === "repeat"
    ? { parent: id, body: "steps" }
    : node.kind === "if"
      ? { parent: id, body: "then" }
      : null;
}

// The gap a drop on a row means: before or after the node, or at the end of what the row holds.
function slotOfDrop(
  index: TreeIndex,
  key: string,
  position: "before" | "after" | "on",
): Slot | null {
  if (position === "on") {
    const body = bodyOf({ key, index });

    if (body === null) {
      return null;
    }

    const container = index.byId.get(body.parent)?.node;
    const list =
      container === undefined
        ? []
        : container.kind === "if"
          ? container[body.body === "else" ? "else" : "then"]
          : container.kind === "group" || container.kind === "repeat"
            ? container.steps
            : [];

    return { parent: body.parent, body: body.body, index: list.length };
  }

  if (key.includes(branchSeparator)) {
    return null;
  }

  const entry = index.byId.get(key);

  return entry === undefined
    ? null
    : {
        parent: entry.parent,
        body: entry.body,
        index: position === "before" ? entry.index : entry.index + 1,
      };
}

// The flow as a tree of rows, the accessible way through it and the flow on a phone. Containers open and close, an IF
// holds its Then and its Else, and a row is dragged to another place, by the pointer or with the keyboard.
export function FlowOutline({
  label,
  steps,
  index,
  selectedId,
  findings,
  locked,
  onSelect,
  onMove,
  className,
}: {
  label: string;
  steps: SequenceStep[];
  index: TreeIndex;
  selectedId: string | null;
  findings: Findings;
  locked: boolean;
  onSelect: (id: string) => void;
  onMove: (ids: string[], slot: Slot) => void;
  className?: string;
}) {
  const { t } = useLingui();
  const rows = rowsOf(steps, "");
  // Containers start open; the ones closed here stay closed while the page is open.
  const [closed, setClosed] = useState<ReadonlySet<Key>>(new Set());
  const expandable: Key[] = [];
  const collect = (list: readonly OutlineRow[]) => {
    for (const row of list) {
      if (row.children.length > 0 || row.kind === "branch" || isContainer(row.node)) {
        expandable.push(row.id);
      }

      collect(row.children);
    }
  };

  collect(rows);

  const expanded = new Set(expandable.filter((key) => !closed.has(key)));

  const { dragAndDropHooks } = useDragAndDrop<OutlineRow>({
    isDisabled: locked,
    getItems: (keys) =>
      [...keys]
        .map(String)
        .filter((key) => !key.includes(branchSeparator))
        .map((key) => {
          const node = index.byId.get(key)?.node;

          return { [NODE_TYPE]: key, "text/plain": node === undefined ? key : nodeTitle(node) };
        }),
    getAllowedDropOperations: () => ["move"],
    getDropOperation: (target, _types, allowed) => {
      if (target.type !== "item" || !allowed.includes("move")) {
        return "cancel";
      }

      const slot = slotOfDrop(index, String(target.key), target.dropPosition);

      return slot === null ? "cancel" : "move";
    },
    renderDropIndicator: (target) => (
      <DropIndicator
        target={target}
        className={({ isDropTarget }) =>
          cx("h-0.5 rounded-full", isDropTarget ? "bg-focus" : "bg-transparent")
        }
      />
    ),
    onMove: (event) => {
      const ids = [...event.keys].map(String).filter((key) => !key.includes(branchSeparator));
      const slot = slotOfDrop(index, String(event.target.key), event.target.dropPosition);

      if (
        slot !== null &&
        ids.length > 0 &&
        !ids.some((id) => slot.parent !== null && isWithin(index, slot.parent, id))
      ) {
        onMove(ids, slot);
      }
    },
  });

  return (
    <Tree
      aria-label={label}
      items={rows}
      selectionMode="single"
      disallowEmptySelection
      selectedKeys={selectedId === null ? [] : [selectedId]}
      onSelectionChange={(keys) => {
        const [key] = keys === "all" ? [] : [...keys];
        const id = key === undefined ? null : String(key).split(branchSeparator)[0];

        if (id !== null && id !== undefined) {
          onSelect(id);
        }
      }}
      expandedKeys={expanded}
      onExpandedChange={(keys) => {
        setClosed(new Set(expandable.filter((key) => !keys.has(key))));
      }}
      dragAndDropHooks={dragAndDropHooks}
      dependencies={[findings, locked]}
      className={cx("flex flex-col gap-0.5 overflow-auto outline-none", className)}
      renderEmptyState={() => (
        <p className="px-3 py-6 type-small text-muted">{t`This sequence has no steps yet.`}</p>
      )}
    >
      {function renderRow(row: OutlineRow) {
        const own = row.kind === "node" ? stepFindings(findings, row.id) : null;
        const counts =
          own === null ? null : findingCounts(own.problems.length, own.warnings.length);
        const title =
          row.kind === "branch" ? (row.branch === "then" ? t`Then` : t`Else`) : nodeTitle(row.node);
        const text =
          row.kind === "node"
            ? nodeLabel(index, row.id, own?.problems.length ?? 0, own?.warnings.length ?? 0)
            : title;

        return (
          <TreeItem
            id={row.id}
            textValue={text}
            aria-label={`${row.path} ${text}`}
            className={cx(
              "group rounded-key outline-none motion-highlight",
              "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus",
              "selected:bg-selected dragging:opacity-50 drop-target:bg-hover",
            )}
          >
            <TreeItemContent>
              {({ hasChildItems, isExpanded, level }) => (
                <div
                  className="flex min-h-10 cursor-pointer items-center gap-2 py-1.5 pr-2"
                  style={{ paddingLeft: `${String((level - 1) * 1.25 + 0.25)}rem` }}
                >
                  {hasChildItems || row.kind === "branch" || isContainer(row.node) ? (
                    <AriaButton
                      slot="chevron"
                      className="flex size-6 shrink-0 cursor-pointer items-center justify-center rounded-key text-muted outline-none hover:bg-hover hover:text-ink"
                    >
                      <IconChevronRight
                        aria-hidden="true"
                        size={14}
                        stroke={2}
                        className={cx("motion-colors", isExpanded && "rotate-90")}
                      />
                    </AriaButton>
                  ) : (
                    <span className="w-6 shrink-0" />
                  )}
                  <span className="w-12 shrink-0 type-small text-muted">{row.path}</span>
                  {row.kind === "node" ? (
                    <NodeGlyph kind={row.node.kind} className="text-ink-2" />
                  ) : null}
                  <span
                    className={cx(
                      "min-w-0 flex-1 truncate",
                      row.kind === "branch" ? "type-label text-ink-2" : "type-body text-ink",
                    )}
                  >
                    {title}
                  </span>
                  {counts === null || own === null ? null : (
                    <StateTag tone={own.problems.length > 0 ? "fail" : "attention"} className="h-5">
                      {counts}
                    </StateTag>
                  )}
                </div>
              )}
            </TreeItemContent>
            <Collection items={row.children}>{renderRow}</Collection>
          </TreeItem>
        );
      }}
    </Tree>
  );
}
