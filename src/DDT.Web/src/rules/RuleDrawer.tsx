// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus, IconX } from "@tabler/icons-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useContext, useId, useState } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import type { Subject } from "@/conditions/conditions";
import { ConditionBuilder } from "@/conditions/ConditionBuilder";
import { ApiError } from "@/lib/api";
import { equalJson } from "@/lib/equalJson";
import type { MachineRoleView } from "@/roles/roles";
import { BuilderContext, useBuilderData } from "@/sequences/builder/builderData";
import { EditorLock } from "@/sequences/editorLock";
import { ChoiceSetting, TextSetting } from "@/sequences/fields";
import { changedCondition } from "@/sequences/flow/conditionTree";
import { fieldFindings, type Findings } from "@/sequences/problems";
import { canRun, type SequenceSummary } from "@/sequences/sequences";
import { Button } from "@/ui/Button";
import { Checkbox } from "@/ui/Checkbox";
import { cx } from "@/ui/cx";
import { ConfirmDialog } from "@/ui/Dialog";
import { Drawer } from "@/ui/Drawer";
import { Menu, MenuItem } from "@/ui/Menu";
import { Notice } from "@/ui/Notice";
import { ValuesEditor } from "@/values/ValuesEditor";
import { rowField } from "@/values/values";

import { DrawerTitle, SavedMeanwhile } from "./drawerParts";
import { noDeclarations } from "./ruleData";
import { conflictOf, noFindings, refusalFindings, unplaced } from "./refusals";
import {
  createRule,
  deleteRule,
  editOf,
  emptyEdit,
  putRule,
  requestOf,
  rulesQuery,
  sequenceResolutionsKey,
  updateRule,
  type RuleEdit,
  type RuleView,
} from "./rules";

const noSequence = "none";

// The fields the drawer shows a finding at; the rest go in a notice at its top.
function isShown(field: string): boolean {
  return /^(name|description|sequenceId|enabled|when|values|roleIds)(\.|\[|$)/.test(field);
}

// A rule's form, beside the list: its name, the condition it applies when, the sequence it chooses, the values it sets
// and the machine roles it gives. The server saves a rule even with problems, which keep it from matching until they
// are fixed, and the drawer then stays open with each problem at its field. Someone who may only look reads the same
// form with nothing to change.
export function RuleDrawer({
  rule,
  count,
  roles,
  sequences,
  subjects,
  canEdit,
  onSaved,
  onClose,
}: {
  // Null for a new rule, which goes to the bottom.
  rule: RuleView | null;
  // How many rules there are, to number a new one.
  count: number;
  roles: readonly MachineRoleView[];
  sequences: readonly SequenceSummary[];
  subjects: readonly Subject[];
  canEdit: boolean;
  // A new rule saved with problems stays open as the rule it now is.
  onSaved: (rule: RuleView) => void;
  onClose: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const builder = useBuilderData(noDeclarations);
  // The rule as last read or saved here, whose revision a save names.
  const [base, setBase] = useState<RuleView | null>(rule);
  const [edit, setEdit] = useState<RuleEdit>(() => (rule === null ? emptyEdit() : editOf(rule)));
  const [findings, setFindings] = useState<Findings>(() =>
    rule === null ? noFindings : { problems: rule.problems, warnings: [] },
  );
  const [theirs, setTheirs] = useState<RuleView | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [gone, setGone] = useState(false);

  const change = (patch: Partial<RuleEdit>) => {
    setEdit((current) => ({ ...current, ...patch }));
  };

  const save = useMutation({
    mutationFn: (revision: number) =>
      base === null
        ? createRule(requestOf(0, edit))
        : updateRule(base.id, requestOf(revision, edit)),
    onSuccess: (saved) => {
      putRule(queryClient, saved);
      // What each machine would get is the server's answer to the rules, so it is asked again.
      void queryClient.invalidateQueries({ queryKey: sequenceResolutionsKey });
      setTheirs(null);

      if (saved.problems.length === 0) {
        onClose();
        return;
      }

      setBase(saved);
      setEdit(editOf(saved));
      setFindings({ problems: saved.problems, warnings: [] });
      onSaved(saved);
    },
    onError: (error) => {
      const current = conflictOf(error) as RuleView | null;

      if (current !== null) {
        putRule(queryClient, current);
        setTheirs(current);
        return;
      }

      if (error instanceof ApiError && error.status === 404) {
        setGone(true);
        return;
      }

      const refused = refusalFindings(error, (field) => rowField(edit.values, field));

      if (refused !== null) {
        setFindings(refused);
      }
    },
  });

  const number = (base?.position ?? count) + 1;
  const name = base?.name ?? "";
  const busy = save.isPending;
  const problemCount = base?.problems.length ?? 0;
  const loose = unplaced(findings, isShown);
  const refused =
    save.isError && refusalFindings(save.error) === null && conflictOf(save.error) === null && !gone
      ? save.error.message
      : null;
  const who = theirs?.updatedBy ?? null;
  const sameCondition = base !== null && equalJson(base.when, edit.when);
  const matching = base?.matchingMachines ?? 0;

  const sequenceChoices = [
    { id: noSequence, label: t`None, a later rule may choose one` },
    ...sequences.map((sequence) => {
      const count = sequence.problemCount;
      const problems = plural(count, { one: "# problem", other: "# problems" });

      return {
        id: sequence.id,
        label: sequence.name,
        // One with problems cannot run, so it cannot be chosen; a rule that chose it keeps it until another is.
        isDisabled: !canRun(sequence) && sequence.id !== edit.sequenceId,
        ...(canRun(sequence) ? {} : { description: t`${problems}, cannot run` }),
      };
    }),
    ...(edit.sequenceId !== null && !sequences.some((sequence) => sequence.id === edit.sequenceId)
      ? [{ id: edit.sequenceId, label: base?.sequenceName ?? t`A sequence that is gone` }]
      : []),
  ];

  return (
    <Drawer
      isOpen
      wide
      onOpenChange={(open) => {
        if (!open && !busy) {
          onClose();
        }
      }}
      title={
        <DrawerTitle over={t`Rule ${number}`}>
          {base === null ? <Trans>New rule</Trans> : name}
        </DrawerTitle>
      }
      footer={
        canEdit ? (
          <>
            <Button
              variant="primary"
              isDisabled={busy || edit.name.trim() === "" || gone}
              onPress={() => {
                save.mutate(base?.revision ?? 0);
              }}
            >
              <Trans>Save rule</Trans>
            </Button>
            <Button variant="secondary" isDisabled={busy} onPress={onClose}>
              <Trans>Cancel</Trans>
            </Button>
            {base !== null && !gone ? (
              <Button
                variant="quiet"
                className="ml-auto text-fail-text hover:text-fail-text"
                isDisabled={busy}
                onPress={() => {
                  setDeleting(true);
                }}
              >
                <Trans>Delete rule</Trans>
              </Button>
            ) : null}
          </>
        ) : (
          <Button variant="secondary" onPress={onClose}>
            <Trans>Close</Trans>
          </Button>
        )
      }
    >
      <BuilderContext value={builder}>
        <EditorLock value={!canEdit}>
          {theirs !== null ? (
            <SavedMeanwhile
              title={
                who === null
                  ? t`Someone else saved this rule while you were editing it.`
                  : t`${who} saved this rule while you were editing it.`
              }
              isBusy={busy}
              onTakeTheirs={() => {
                setBase(theirs);
                setEdit(editOf(theirs));
                setFindings({ problems: theirs.problems, warnings: [] });
                setTheirs(null);
                save.reset();
              }}
              onKeepMine={() => {
                setBase(theirs);
                save.mutate(theirs.revision);
              }}
            />
          ) : null}

          {gone ? (
            <Notice tone="attention">
              <Trans>Someone deleted this rule while you were editing it.</Trans>
            </Notice>
          ) : null}

          {problemCount > 0 && theirs === null ? (
            <Notice tone="attention">
              {plural(problemCount, {
                one: "This rule has # problem, shown at its field. It matches no machine until it is fixed.",
                other:
                  "This rule has # problems, shown at their fields. It matches no machine until they are fixed.",
              })}
            </Notice>
          ) : null}

          {loose.length > 0 ? <Notice tone="fail">{loose.join(" ")}</Notice> : null}
          {refused !== null ? <Notice tone="fail">{refused}</Notice> : null}

          <TextSetting
            label={<Trans>Name</Trans>}
            field="name"
            findings={findings}
            value={edit.name}
            onChange={(text) => {
              change({ name: text });
            }}
          />

          <div className="flex flex-col gap-1.5">
            <ConditionBuilder
              label={<Trans>Applies when</Trans>}
              use="rule"
              value={edit.when}
              subjects={subjects}
              findings={findings}
              onChange={(path, conditionChange) => {
                const next = changedCondition(edit.when, path, conditionChange);

                if (next !== undefined) {
                  change({ when: next });
                }
              }}
            />
            {base === null ? null : sameCondition ? (
              <p className="type-small text-muted">
                {matching === 0
                  ? t`Matches no known machine now.`
                  : plural(matching, {
                      one: "Matches # known machine now.",
                      other: "Matches # known machines now.",
                    })}
              </p>
            ) : (
              <p className="type-small text-muted">
                <Trans>Save the rule to count the machines it matches.</Trans>
              </p>
            )}
          </div>

          <ChoiceSetting
            label={<Trans>Chooses a task sequence</Trans>}
            field="sequenceId"
            findings={findings}
            value={edit.sequenceId ?? noSequence}
            choices={sequenceChoices}
            onChange={(id) => {
              change({ sequenceId: id === noSequence ? null : id });
            }}
          />

          <ValuesEditor
            label={<Trans>Sets values</Trans>}
            hint={
              <Trans>
                A value a rule above sets already stays as that rule sets it; a sequence uses it by
                its name.
              </Trans>
            }
            rows={edit.values}
            findings={findings}
            onChange={(values) => {
              change({ values });
            }}
          />

          <RolesPicker
            roleIds={edit.roleIds}
            roles={roles}
            findings={findings}
            onChange={(roleIds) => {
              change({ roleIds });
            }}
          />

          <TextSetting
            label={<Trans>Description</Trans>}
            hint={<Trans>Optional. Why the rule exists, for whoever reads it later.</Trans>}
            field="description"
            findings={findings}
            value={edit.description}
            onChange={(text) => {
              change({ description: text });
            }}
          />

          <div data-field="enabled">
            <Checkbox
              isSelected={edit.enabled}
              isReadOnly={!canEdit}
              onChange={(enabled) => {
                change({ enabled });
              }}
            >
              <span className="flex flex-col">
                <span>
                  <Trans>Check machines against this rule</Trans>
                </span>
                <span className="type-small text-muted">
                  <Trans>Turned off, the rule is kept but matches no machine.</Trans>
                </span>
              </span>
            </Checkbox>
          </div>
        </EditorLock>
      </BuilderContext>

      {deleting && base !== null ? (
        <DeleteRuleDialog
          rule={base}
          onClose={() => {
            setDeleting(false);
          }}
          onDeleted={onClose}
        />
      ) : null}
    </Drawer>
  );
}

// Asks before deleting a rule, and says what that does. The answer is the list, whose places changed.
export function DeleteRuleDialog({
  rule,
  onClose,
  onDeleted,
}: {
  rule: RuleView;
  onClose: () => void;
  onDeleted: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const id = rule.id;
  const name = rule.name;

  const remove = useMutation({
    mutationFn: () => deleteRule(id),
    onSuccess: (list) => {
      queryClient.setQueryData(rulesQuery.queryKey, list);
      void queryClient.invalidateQueries({ queryKey: sequenceResolutionsKey });
      onDeleted();
    },
    // Someone else deleted it first, which is what was asked for.
    onError: (error) => {
      if (error instanceof ApiError && error.status === 404) {
        queryClient.setQueryData(rulesQuery.queryKey, (list) =>
          list?.filter((existing) => existing.id !== id),
        );
        onDeleted();
      }
    },
  });

  return (
    <ConfirmDialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={t`Delete ${name}?`}
      confirmLabel={<Trans>Delete rule</Trans>}
      danger
      isBusy={remove.isPending}
      error={remove.isError ? remove.error.message : undefined}
      onConfirm={() => {
        remove.mutate();
      }}
    >
      <p>
        <Trans>
          The machines it matches no longer get what it chooses, sets or gives; a rule below it may
          give them that instead.
        </Trans>
      </p>
      <p>
        <Trans>
          The rules below it move up a place. Runs that already started keep their values.
        </Trans>
      </p>
    </ConfirmDialog>
  );
}

// The machine roles a rule gives, as tags that can be taken out, and a menu of the others to add.
function RolesPicker({
  roleIds,
  roles,
  findings,
  onChange,
}: {
  roleIds: readonly string[];
  roles: readonly MachineRoleView[];
  findings: Findings;
  onChange: (roleIds: string[]) => void;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const labelId = useId();
  const others = roles.filter((role) => !roleIds.includes(role.id));
  const messages = [
    ...fieldFindings(findings, "roleIds").problems,
    ...roleIds.flatMap((_, index) => fieldFindings(findings, `roleIds[${String(index)}]`).problems),
  ];

  return (
    <div
      role="group"
      aria-labelledby={labelId}
      data-field="roleIds"
      className="flex flex-col gap-2"
    >
      <span id={labelId} className="type-label text-ink">
        <Trans>Gives machine roles</Trans>
      </span>
      <div className="flex flex-wrap items-center gap-1.5">
        {roleIds.map((id, index) => {
          const role = roles.find((candidate) => candidate.id === id);
          const roleName = role?.name ?? t`A machine role that is gone`;
          const problem = fieldFindings(findings, `roleIds[${String(index)}]`).problems.length > 0;

          return (
            <span
              key={id}
              data-field={`roleIds[${String(index)}]`}
              className={cx(
                "inline-flex h-7 items-center gap-1 rounded-key pr-1 pl-2.5 type-body text-ink",
                problem
                  ? "shadow-[inset_0_0_0_1.5px_var(--color-fail-text)]"
                  : "shadow-[inset_0_0_0_1px_var(--color-line)]",
                locked && "pr-2.5",
              )}
            >
              {roleName}
              {locked ? null : (
                <AriaButton
                  aria-label={t`Take ${roleName} out`}
                  className="flex size-5 cursor-pointer items-center justify-center rounded-tag text-muted key-motion outline-none hover:bg-hover hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
                  onPress={() => {
                    onChange(roleIds.filter((other) => other !== id));
                  }}
                >
                  <IconX size={12} stroke={2} />
                </AriaButton>
              )}
            </span>
          );
        })}
        {!locked && others.length > 0 ? (
          <MenuTrigger>
            <AriaButton className="flex cursor-pointer items-center gap-1.5 rounded-key px-1.5 py-1 type-label text-ink key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus">
              <IconPlus aria-hidden="true" size={14} stroke={2} />
              <Trans>Add a role</Trans>
            </AriaButton>
            <Menu
              placement="bottom start"
              aria-label={t`Machine roles to add`}
              onAction={(key) => {
                onChange([...roleIds, String(key)]);
              }}
            >
              {others.map((role) => (
                <MenuItem key={role.id} id={role.id} textValue={role.name}>
                  {role.name}
                </MenuItem>
              ))}
            </Menu>
          </MenuTrigger>
        ) : null}
      </div>
      {roleIds.length === 0 && (locked || roles.length > 0) ? (
        <p className="type-small text-muted">
          <Trans>No machine roles.</Trans>
        </p>
      ) : null}
      {!locked && roles.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>
            There are no machine roles yet. Add them under{" "}
            <Link to="/deployment/machine-roles" className="text-ink underline">
              Deployment, Machine roles
            </Link>
            .
          </Trans>
        </p>
      ) : null}
      {messages.map((message) => (
        <p key={message} className="type-small text-fail-text">
          {message}
        </p>
      ))}
    </div>
  );
}
