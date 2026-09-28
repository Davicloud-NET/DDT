// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconDots, IconGripVertical, IconPlus } from "@tabler/icons-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { useState, type KeyboardEvent } from "react";
import {
  Button as AriaButton,
  DropIndicator,
  GridList,
  GridListItem,
  Keyboard,
  MenuTrigger,
  Text,
  useDragAndDrop,
} from "react-aria-components";

import { currentUserQuery } from "@/auth/auth";
import { conditionSentence, type Subject } from "@/conditions/conditions";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";
import { machineRolesQuery, type MachineRoleView } from "@/roles/roles";
import { sequencesQuery } from "@/sequences/sequences";
import { Button } from "@/ui/Button";
import { cx } from "@/ui/cx";
import { EmptyState, Page, Panel, Skeleton } from "@/ui/Layout";
import { Menu, MenuItem } from "@/ui/Menu";
import { Notice } from "@/ui/Notice";
import { StateTag } from "@/ui/StateTag";

import { useRuleSubjects } from "./ruleData";
import { DeleteRuleDialog, RuleDrawer } from "./RuleDrawer";
import {
  droppedAt,
  inOrder,
  movedBy,
  reorderRules,
  RulesChangedMeanwhile,
  ruleEffects,
  ruleName,
  rulesQuery,
  sequenceResolutionsKey,
  type RuleView,
} from "./rules";
import { RuleTest } from "./RuleTest";

// The rules, one ordered list checked from the top: the first rule that chooses a sequence chooses it, the first that
// sets a value sets it, and every rule that matches gives its machine roles. An administrator adds and changes them in
// a drawer and moves them by dragging, with Alt and the arrow keys, or from a rule's menu; each move sends the whole
// order, and the list shows it at once and takes the server's answer, or the list as it is when someone changed it
// meanwhile. The list is live, and a rule someone else changed flashes.
export function RulesPage() {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const freshness = liveListOptions(useLiveStatus());
  const rules = useQuery({ ...rulesQuery, ...freshness });
  const roles = useQuery({ ...machineRolesQuery, ...freshness });
  const sequences = useQuery({ ...sequencesQuery, ...freshness });
  const subjects = useRuleSubjects();
  const user = useQuery(currentUserQuery).data ?? null;
  const canEdit = user?.roles.includes("Administrator") === true;
  const search = useSearch({ from: "/shell/deployment/rules" });
  const navigate = useNavigate({ from: "/deployment/rules" });
  const mark = useLiveMarks({
    queryKey: rulesQuery.queryKey,
    items: (list) => list,
    id: (rule) => rule.id,
    signature: (rule) => `${String(rule.revision)} ${String(rule.position)}`,
    tone: () => "idle",
  });

  // The drawer's rule as it was when it opened, null for a new one; key opens a fresh form each time.
  const [drawer, setDrawer] = useState<{ key: number; rule: RuleView | null } | null>(null);
  // A rule named in the address, such as from a machine role's page, opens once the list is there.
  const [linked, setLinked] = useState(search.rule ?? null);
  const [deleting, setDeleting] = useState<RuleView | null>(null);
  const [moveProblem, setMoveProblem] = useState<string | null>(null);

  const list = rules.data ?? [];
  const roleList = roles.data ?? [];

  if (linked !== null && rules.data !== undefined) {
    const found = rules.data.find((rule) => rule.id === linked);

    setLinked(null);

    if (found !== undefined) {
      setDrawer({ key: 1, rule: found });
    }
  }

  const openRule = (rule: RuleView | null) => {
    setDrawer((current) => ({ key: (current?.key ?? 0) + 1, rule }));
  };

  const closeDrawer = () => {
    setDrawer(null);

    if (search.rule !== undefined) {
      void navigate({ search: {}, replace: true });
    }
  };

  const move = useMutation({
    mutationFn: (order: string[]) => reorderRules(order),
    onMutate: async (order) => {
      await queryClient.cancelQueries({ queryKey: rulesQuery.queryKey });
      const before = queryClient.getQueryData(rulesQuery.queryKey);

      queryClient.setQueryData(rulesQuery.queryKey, (current) =>
        current === undefined ? current : inOrder(current, order),
      );
      setMoveProblem(null);

      return { before };
    },
    onSuccess: (answer) => {
      queryClient.setQueryData(rulesQuery.queryKey, answer);
      void queryClient.invalidateQueries({ queryKey: sequenceResolutionsKey });
    },
    // A 409 carries the list as it is now, which someone changed meanwhile; anything else puts the order back.
    onError: (error, _order, context) => {
      const current = error instanceof RulesChangedMeanwhile ? error.rules : null;

      queryClient.setQueryData(rulesQuery.queryKey, current ?? context?.before);

      if (current !== null) {
        setMoveProblem(
          t`Someone changed the rules while you moved one, so the list shows them as they are now. Move the rule again if it should still go there.`,
        );
      } else {
        const message = error.message;

        setMoveProblem(t`The rule could not be moved: ${message}`);
      }
    },
  });

  const moveBy = (id: string, offset: number) => {
    const order = movedBy(list, id, offset);

    if (order !== null) {
      move.mutate(order);
    }
  };

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
        move.mutate(order);
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
      moveBy(id, event.key === "ArrowUp" ? -1 : 1);
    }
  };

  const openId = drawer?.rule?.id ?? null;

  return (
    <Page className="max-w-230">
      <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
        <div className="flex max-w-155 flex-col gap-2.5">
          <h1 className="type-title text-ink">
            <Trans>Rules</Trans>
          </h1>
          <p className="text-ink-2">
            <Trans>
              Checked from the top. The first rule that chooses a sequence chooses it, and the first
              rule that sets a value sets it. A rule never approves a machine.
            </Trans>
          </p>
        </div>
        {canEdit ? (
          <Button
            variant="primary"
            onPress={() => {
              openRule(null);
            }}
          >
            <IconPlus aria-hidden="true" size={14} stroke={2} />
            <Trans>Add rule</Trans>
          </Button>
        ) : null}
      </div>

      {rules.isError ? (
        <Notice tone="fail">
          <Trans>The rules could not be loaded.</Trans>
        </Notice>
      ) : null}

      {moveProblem !== null ? <Notice tone="attention">{moveProblem}</Notice> : null}

      {rules.isPending ? (
        <Panel>
          <Skeleton className="h-6 w-1/3" />
          <Skeleton className="h-6 w-2/3" />
          <Skeleton className="h-6 w-1/2" />
        </Panel>
      ) : rules.isSuccess && list.length === 0 ? (
        <Panel>
          <EmptyState title={<Trans>No rules yet</Trans>}>
            {canEdit ? (
              <Trans>
                Without rules an operator chooses the sequence of each machine. Add a rule to choose
                one, set values or give machine roles by what a machine is, such as its model or its
                network.
              </Trans>
            ) : (
              <Trans>
                Without rules an operator chooses the sequence of each machine. An administrator
                adds rules here.
              </Trans>
            )}
          </EmptyState>
        </Panel>
      ) : rules.isSuccess ? (
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
                openRule(rule);
              }
            }}
            dependencies={[mark, canEdit, roleList, subjects, openId, list.length]}
            className="flex flex-col outline-none"
          >
            {(rule) => (
              <RuleRow
                rule={rule}
                last={rule.position === list.length - 1}
                roles={roleList}
                subjects={subjects}
                canEdit={canEdit}
                isOpen={rule.id === openId}
                mark={mark(rule.id)}
                onOpen={() => {
                  openRule(rule);
                }}
                onMove={(offset) => {
                  moveBy(rule.id, offset);
                }}
                onDelete={() => {
                  setDeleting(rule);
                }}
              />
            )}
          </GridList>
        </div>
      ) : null}

      {rules.isSuccess ? <RuleTest rules={list} /> : null}

      {drawer !== null ? (
        <RuleDrawer
          key={drawer.key}
          rule={drawer.rule}
          count={list.length}
          roles={roleList}
          sequences={sequences.data ?? []}
          subjects={subjects}
          canEdit={canEdit}
          onSaved={(saved) => {
            setDrawer((current) => (current === null ? current : { ...current, rule: saved }));
          }}
          onClose={closeDrawer}
        />
      ) : null}

      {deleting !== null ? (
        <DeleteRuleDialog
          rule={deleting}
          onClose={() => {
            setDeleting(null);
          }}
          onDeleted={() => {
            setDeleting(null);
          }}
        />
      ) : null}
    </Page>
  );
}

function RuleRow({
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
}: {
  rule: RuleView;
  last: boolean;
  roles: readonly MachineRoleView[];
  subjects: readonly Subject[];
  canEdit: boolean;
  isOpen: boolean;
  mark: string;
  onOpen: () => void;
  onMove: (offset: number) => void;
  onDelete: () => void;
}) {
  const { t } = useLingui();
  const name = ruleName(rule);
  const number = String(rule.position + 1).padStart(2, "0");
  const sentence = conditionSentence("rule", rule.when, subjects);
  const effects = ruleEffects(rule, roles);
  const problems = rule.problems.length;
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
      <div
        className={cx(
          "row-start-1 flex min-w-0 flex-col gap-1.25",
          canEdit ? "col-start-3" : "col-start-2",
        )}
      >
        <span className="flex min-w-0 items-center gap-2">
          <span className={cx("truncate type-label", rule.enabled ? "text-ink" : "text-ink-2")}>
            {rule.name}
          </span>
          {rule.enabled ? null : (
            <StateTag tone="idle" className="h-5">
              <Trans>Off</Trans>
            </StateTag>
          )}
          {problems > 0 ? (
            <StateTag tone="fail" className="h-5">
              {plural(problems, { one: "# problem", other: "# problems" })}
            </StateTag>
          ) : null}
        </span>
        <span
          className="line-clamp-2 type-small break-words text-muted sm:line-clamp-1"
          title={sentence}
        >
          {sentence}
        </span>
        {effects.length > 0 ? (
          <span className="flex flex-wrap gap-1.5">
            {effects.map((effect, index) => (
              <span
                key={index}
                className="inline-flex h-5.5 max-w-full items-center truncate rounded-tag px-2 type-small whitespace-nowrap text-ink-2 shadow-[inset_0_0_0_1px_var(--color-line)]"
              >
                {effect}
              </span>
            ))}
          </span>
        ) : null}
      </div>
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

function RuleMenu({
  name,
  first,
  last,
  onOpen,
  onMove,
  onDelete,
}: {
  name: string;
  first: boolean;
  last: boolean;
  onOpen: () => void;
  onMove: (offset: number) => void;
  onDelete: () => void;
}) {
  const { t } = useLingui();
  const label = t`Actions for ${name}`;

  return (
    <MenuTrigger>
      <AriaButton
        aria-label={label}
        className="col-start-4 row-span-2 row-start-1 flex size-8 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-ink focus-visible:outline-2 focus-visible:outline-focus sm:col-start-5 sm:row-span-1"
      >
        <IconDots size={18} stroke={2} />
      </AriaButton>
      <Menu
        aria-label={label}
        disabledKeys={[...(first ? ["up"] : []), ...(last ? ["down"] : [])]}
        onAction={(key) => {
          switch (key) {
            case "edit":
              onOpen();
              break;
            case "up":
              onMove(-1);
              break;
            case "down":
              onMove(1);
              break;
            case "delete":
              onDelete();
              break;
          }
        }}
      >
        <MenuItem id="edit">
          <Trans>Change</Trans>
        </MenuItem>
        <MenuItem id="up" textValue={t`Move up`}>
          <Text slot="label" className="flex-1">
            <Trans>Move up</Trans>
          </Text>
          <Keyboard className="type-small text-muted">Alt+↑</Keyboard>
        </MenuItem>
        <MenuItem id="down" textValue={t`Move down`}>
          <Text slot="label" className="flex-1">
            <Trans>Move down</Trans>
          </Text>
          <Keyboard className="type-small text-muted">Alt+↓</Keyboard>
        </MenuItem>
        <MenuItem id="delete" className="text-fail-text">
          <Trans>Delete</Trans>
        </MenuItem>
      </Menu>
    </MenuTrigger>
  );
}
