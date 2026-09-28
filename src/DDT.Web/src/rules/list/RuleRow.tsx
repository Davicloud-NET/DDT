// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";
import { IconGripVertical } from "@tabler/icons-react";
import { Button as AriaButton, GridListItem } from "react-aria-components";

import { conditionSentence } from "@/conditions/conditions";
import type { Subject } from "@/conditions/conditionSubjects";
import type { MachineRoleView } from "@/roles/roles";
import { cx } from "@/ui/cx";

import type { RuleView } from "../rules";
import { ruleEffects, ruleName } from "../ruleText";
import { RuleMenu } from "./RuleMenu";
import { RuleSummary } from "./RuleSummary";

interface RuleRowProps {
  rule: RuleView;
  last: boolean;
  roles: readonly MachineRoleView[];
  subjects: readonly Subject[];
  canEdit: boolean;
  isOpen: boolean;
  // The live mark's classes.
  mark: string;
  onOpen: () => void;
  onMove: (offset: number) => void;
  onDelete: () => void;
}

// One rule of the ordered list, with its drag handle and menu for someone who may change it.
export function RuleRow({
  rule,
  last,
  roles,
  subjects,
  canEdit,
  isOpen,
  mark,
  onOpen,
  onMove,
  onDelete,
}: RuleRowProps) {
  const { t } = useLingui();
  const name = ruleName(rule);
  const number = String(rule.position + 1).padStart(2, "0");
  const sentence = conditionSentence("rule", rule.when, subjects);
  const effects = ruleEffects(rule, roles);
  const matching = rule.matchingMachines;
  const count =
    matching === 0 ? t`No machine` : plural(matching, { one: "# machine", other: "# machines" });
  // What a screen reader says of the row: its name, its condition, what it does and what it matches.
  const text = [name, sentence, effects.join(", "), count].filter((part) => part !== "").join(". ");

  return (
    <GridListItem
      id={rule.id}
      textValue={text}
      data-rule-id={rule.id}
      className={({ isFocusVisible, isDragging }) =>
        cx(
          "grid cursor-pointer items-center gap-x-3 gap-y-1 px-4 py-3 shadow-[inset_0_-1px_0_var(--color-line-soft)] outline-none motion-highlight hover:bg-hover",
          // On a phone the count goes under the rule, so the rule keeps the width.
          canEdit
            ? "grid-cols-[1.375rem_2.125rem_minmax(0,1fr)_2rem] sm:grid-cols-[1.375rem_2.125rem_minmax(0,1fr)_6rem_2rem]"
            : "grid-cols-[2.125rem_minmax(0,1fr)] sm:grid-cols-[2.125rem_minmax(0,1fr)_6rem]",
          isOpen && "bg-selected hover:bg-selected",
          isFocusVisible && "outline-2 -outline-offset-2 outline-focus",
          isDragging && "opacity-50",
          mark,
        )
      }
    >
      {canEdit ? (
        <AriaButton
          slot="drag"
          aria-label={t`Move ${name}`}
          className="row-span-2 flex h-8 w-5.5 cursor-grab items-center justify-center rounded-key text-control outline-none hover:bg-hover hover:text-ink focus-visible:outline-2 focus-visible:outline-focus sm:row-span-1"
        >
          <IconGripVertical aria-hidden="true" size={16} stroke={2} />
        </AriaButton>
      ) : null}
      <span className="row-span-2 type-numeral text-ink-2 tabular-nums sm:row-span-1">
        {number}
      </span>
      <RuleSummary rule={rule} sentence={sentence} effects={effects} canEdit={canEdit} />
      <span
        className={cx(
          "row-start-2 type-small text-muted sm:row-start-1 sm:text-right",
          canEdit ? "col-start-3 sm:col-start-4" : "col-start-2 sm:col-start-3",
        )}
      >
        {count}
      </span>
      {canEdit ? (
        <RuleMenu
          name={name}
          first={rule.position === 0}
          last={last}
          onOpen={onOpen}
          onMove={onMove}
          onDelete={onDelete}
        />
      ) : null}
    </GridListItem>
  );
}
