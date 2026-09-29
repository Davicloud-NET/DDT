// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import type { KeyboardEvent } from "react";
import { DropIndicator, GridList, useDragAndDrop } from "react-aria-components";

import type { Subject } from "@/conditions/conditionSubjects";
import type { MachineRoleView } from "@/roles/roles";
import { cx } from "@/ui/cx";

import { droppedAt } from "../ruleOrder";
import type { RuleView } from "../rules";
import { ruleName } from "../ruleText";
import { RuleRow } from "./RuleRow";

interface RuleListProps {
  list: readonly RuleView[];
  roles: readonly MachineRoleView[];
  subjects: readonly Subject[];
  canEdit: boolean;
  // The rule whose drawer is open.
  openId: string | null;
  mark: (id: string) => string;
  onOpen: (rule: RuleView) => void;
  // Every rule's id in the new order, top first.
  onReorder: (order: string[]) => void;
  onMoveBy: (id: string, offset: number) => void;
  onDelete: (rule: RuleView) => void;
}

// The rules in order. Someone who may change them can move them by dragging, with Alt and the arrow keys, or from a
// rule's menu.
export function RuleList({
  list,
  roles,
  subjects,
  canEdit,
  openId,
  mark,
  onOpen,
  onReorder,
  onMoveBy,
  onDelete,
}: RuleListProps) {
  const { t } = useLingui();

  const { dragAndDropHooks } = useDragAndDrop<RuleView>({
    isDisabled: !canEdit,
    getItems: (keys) =>
      [...keys].map((key) => {
        const rule = list.find((candidate) => candidate.id === String(key));

        return { "text/plain": rule === undefined ? String(key) : ruleName(rule) };
      }),
    onReorder: (event) => {
      const order = droppedAt(
        list,
        [...event.keys].map(String),
        String(event.target.key),
        event.target.dropPosition === "before" ? "before" : "after",
      );

      if (order !== null) {
        onReorder(order);
      }
    },
    renderDropIndicator: (target) => (
      <DropIndicator
        target={target}
        className={({ isDropTarget }) =>
          cx("h-0.5 rounded-full", isDropTarget ? "bg-focus" : "bg-transparent")
        }
      />
    ),
  });

  // Alt and an arrow key move the rule whose row has the focus, as in the outline of a sequence.
  const onKeyDownCapture = (event: KeyboardEvent) => {
    if (!canEdit || !event.altKey || (event.key !== "ArrowUp" && event.key !== "ArrowDown")) {
      return;
    }

    const id = (event.target as HTMLElement).closest<HTMLElement>("[data-rule-id]")?.dataset.ruleId;

    if (id !== undefined) {
      event.preventDefault();
      event.stopPropagation();
      onMoveBy(id, event.key === "ArrowUp" ? -1 : 1);
    }
  };

  return (
    <div
      className="overflow-hidden rounded-panel bg-panel shadow-panel"
      onKeyDownCapture={onKeyDownCapture}
    >
      <GridList
        aria-label={t`Rules in order`}
        items={list}
        {...(canEdit ? { dragAndDropHooks } : {})}
        onAction={(key) => {
          const rule = list.find((candidate) => candidate.id === String(key));

          if (rule !== undefined) {
            onOpen(rule);
          }
        }}
        dependencies={[mark, canEdit, roles, subjects, openId, list.length]}
        className="flex flex-col outline-none"
      >
        {(rule) => (
          <RuleRow
            rule={rule}
            last={rule.position === list.length - 1}
            roles={roles}
            subjects={subjects}
            canEdit={canEdit}
            isOpen={rule.id === openId}
            mark={mark(rule.id)}
            onOpen={() => {
              onOpen(rule);
            }}
            onMove={(offset) => {
              onMoveBy(rule.id, offset);
            }}
            onDelete={() => {
              onDelete(rule);
            }}
          />
        )}
      </GridList>
    </div>
  );
}
