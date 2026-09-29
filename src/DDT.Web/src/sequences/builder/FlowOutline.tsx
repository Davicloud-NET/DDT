// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { useState } from "react";
import {
  Collection,
  DropIndicator,
  Tree,
  TreeItem,
  TreeItemContent,
  useDragAndDrop,
  type Key,
} from "react-aria-components";

import { cx } from "@/ui/cx";

import { nodeLabel, nodeTitle } from "../flow/flowLabels";
import type { Slot, TreeIndex } from "../flow/flowTree";
import { stepFindings, type Findings } from "../problems";
import type { SequenceStep } from "../sequences";
import { outlineDragItems, outlineDropOperation, outlineMove } from "./outline/outlineDrop";
import { OutlineRowContent } from "./outline/OutlineRowContent";
import { BRANCH_SEPARATOR, expandableKeys, rowsOf, type OutlineRow } from "./outline/outlineRows";

interface FlowOutlineProps {
  label: string;
  steps: SequenceStep[];
  index: TreeIndex;
  selectedId: string | null;
  findings: Findings;
  locked: boolean;
  onSelect: (id: string) => void;
  onMove: (ids: string[], slot: Slot) => void;
  className?: string;
}

// The flow as a tree of rows. It's the accessible way through the flow, and the view on a phone. Rows move by drag
// and drop, with the pointer or the keyboard.
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
}: FlowOutlineProps) {
  const { t } = useLingui();
  const rows = rowsOf(steps, "");
  // Containers start open. The ones closed here stay closed while the page is open.
  const [closed, setClosed] = useState<ReadonlySet<Key>>(new Set());
  const expandable = expandableKeys(rows);
  const expanded = new Set(expandable.filter((key) => !closed.has(key)));

  const { dragAndDropHooks } = useDragAndDrop<OutlineRow>({
    isDisabled: locked,
    getItems: (keys) => outlineDragItems(index, keys),
    getAllowedDropOperations: () => ["move"],
    getDropOperation: (target, _types, allowed) => outlineDropOperation(index, target, allowed),
    renderDropIndicator: (target) => (
      <DropIndicator
        target={target}
        className={({ isDropTarget }) =>
          cx("h-0.5 rounded-full", isDropTarget ? "bg-focus" : "bg-transparent")
        }
      />
    ),
    onMove: (event) => {
      const move = outlineMove(index, event.keys, event.target);

      if (move !== null) {
        onMove(move.ids, move.slot);
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
        const id = key === undefined ? null : String(key).split(BRANCH_SEPARATOR)[0];

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
                <OutlineRowContent
                  row={row}
                  title={title}
                  own={own}
                  locked={locked}
                  hasChildItems={hasChildItems}
                  isExpanded={isExpanded}
                  level={level}
                />
              )}
            </TreeItemContent>
            <Collection items={row.children}>{renderRow}</Collection>
          </TreeItem>
        );
      }}
    </Tree>
  );
}
