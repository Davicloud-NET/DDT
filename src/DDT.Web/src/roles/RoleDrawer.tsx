// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useState } from "react";

import { ApiError } from "@/lib/api";
import { DrawerTitle, SavedMeanwhile } from "@/rules/drawerParts";
import { noDeclarations } from "@/rules/ruleData";
import { conflictOf, noFindings, refusalFindings, unplaced } from "@/rules/refusals";
import {
  ruleName,
  ruleNames,
  rulesQuery,
  sequenceResolutionsKey,
  type RuleView,
} from "@/rules/rules";
import { BuilderContext, useBuilderData } from "@/sequences/builder/builderData";
import { EditorLock } from "@/sequences/editorLock";
import { TextSetting } from "@/sequences/fields";
import type { Findings } from "@/sequences/problems";
import { Button } from "@/ui/Button";
import { ConfirmDialog } from "@/ui/Dialog";
import { Drawer } from "@/ui/Drawer";
import { Notice } from "@/ui/Notice";
import { ValuesEditor } from "@/values/ValuesEditor";
import { rowField } from "@/values/values";

import {
  createRole,
  deleteRole,
  putRole,
  removeRole,
  roleEditOf,
  roleRequestOf,
  rulesGiving,
  updateRole,
  type MachineRoleView,
  type RoleEdit,
} from "./roles";

function isShown(field: string): boolean {
  return /^(name|description|values)(\.|\[|$)/.test(field);
}

// A machine role's form: its name, what it is for, and the values it sets. The server refuses values a run could not
// use, and says so at each value. Someone who may only look reads the same form with nothing to change.
export function RoleDrawer({
  role,
  rules,
  canEdit,
  onClose,
}: {
  // Null for a new role.
  role: MachineRoleView | null;
  rules: readonly RuleView[];
  canEdit: boolean;
  onClose: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const builder = useBuilderData(noDeclarations);
  const [base, setBase] = useState<MachineRoleView | null>(role);
  const [edit, setEdit] = useState<RoleEdit>(() => roleEditOf(role));
  const [findings, setFindings] = useState<Findings>(noFindings);
  const [theirs, setTheirs] = useState<MachineRoleView | null>(null);
  const [gone, setGone] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const change = (patch: Partial<RoleEdit>) => {
    setEdit((current) => ({ ...current, ...patch }));
  };

  const save = useMutation({
    mutationFn: (revision: number) =>
      base === null
        ? createRole(roleRequestOf(0, edit))
        : updateRole(base.id, roleRequestOf(revision, edit)),
    onSuccess: (saved) => {
      putRole(queryClient, saved);
      void queryClient.invalidateQueries({ queryKey: sequenceResolutionsKey });
      onClose();
    },
    onError: (error) => {
      const current = conflictOf(error) as MachineRoleView | null;

      if (current !== null) {
        putRole(queryClient, current);
        setTheirs(current);
        return;
      }

      if (error instanceof ApiError && error.status === 404) {
        setGone(true);
        return;
      }

      setFindings(refusalFindings(error, (field) => rowField(edit.values, field)) ?? noFindings);
    },
  });

  const name = base?.name ?? "";
  const busy = save.isPending;
  const loose = unplaced(findings, isShown);
  const refused =
    save.isError && refusalFindings(save.error) === null && conflictOf(save.error) === null && !gone
      ? save.error.message
      : null;
  const who = theirs?.updatedBy ?? null;
  const giving = base === null ? [] : rulesGiving(rules, base.id);

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
        <DrawerTitle over={t`Machine role`}>
          {base === null ? <Trans>New machine role</Trans> : name}
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
              <Trans>Save machine role</Trans>
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
                <Trans>Delete machine role</Trans>
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
                  ? t`Someone else saved this machine role while you were editing it.`
                  : t`${who} saved this machine role while you were editing it.`
              }
              isBusy={busy}
              onTakeTheirs={() => {
                setBase(theirs);
                setEdit(roleEditOf(theirs));
                setFindings(noFindings);
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
              <Trans>Someone deleted this machine role while you were editing it.</Trans>
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

          <TextSetting
            label={<Trans>Description</Trans>}
            hint={
              <Trans>
                Optional. What machines with this role are, such as kiosks in the lobby.
              </Trans>
            }
            field="description"
            findings={findings}
            value={edit.description}
            onChange={(text) => {
              change({ description: text });
            }}
          />

          <ValuesEditor
            label={<Trans>Sets values</Trans>}
            hint={
              <Trans>
                A machine gets them from each rule that gives it the role, after the values the
                rules set themselves.
              </Trans>
            }
            rows={edit.values}
            findings={findings}
            onChange={(values) => {
              change({ values });
            }}
          />

          {base !== null ? (
            <div className="flex flex-col gap-1.5">
              <span className="type-label text-ink">
                <Trans>Given by</Trans>
              </span>
              {giving.length === 0 ? (
                <p className="type-small text-muted">
                  <Trans>No rule gives this machine role yet.</Trans>
                </p>
              ) : (
                <RuleLinks rules={giving} />
              )}
            </div>
          ) : null}
        </EditorLock>
      </BuilderContext>

      {deleting && base !== null ? (
        <DeleteRoleDialog
          role={base}
          rules={rules}
          onClose={() => {
            setDeleting(false);
          }}
          onDeleted={onClose}
        />
      ) : null}
    </Drawer>
  );
}

// The rules that give a role, each a link that opens it on the rules page.
export function RuleLinks({ rules }: { rules: readonly RuleView[] }) {
  return (
    <ul className="flex flex-col gap-0.5">
      {rules.map((rule) => (
        <li key={rule.id} className="min-w-0 truncate">
          <Link
            to="/deployment/rules"
            search={{ rule: rule.id }}
            className="text-ink hover:underline"
          >
            {ruleName(rule)}
          </Link>
        </li>
      ))}
    </ul>
  );
}

// Asks before deleting a machine role. While rules give it the server keeps it, so the dialog says which rules to
// change instead of offering the deletion; one that became so meanwhile is refused with 409, said the same way.
export function DeleteRoleDialog({
  role,
  rules,
  onClose,
  onDeleted,
}: {
  role: MachineRoleView;
  rules: readonly RuleView[];
  onClose: () => void;
  onDeleted: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const id = role.id;
  const name = role.name;

  const remove = useMutation({
    mutationFn: () => deleteRole(id),
    onSuccess: () => {
      removeRole(queryClient, id);
      onDeleted();
    },
    onError: (error) => {
      // Someone else deleted it first, which is what was asked for.
      if (error instanceof ApiError && error.status === 404) {
        removeRole(queryClient, id);
        onDeleted();
      }

      // A rule gives it that this page does not know of yet, so the rules are read to name it.
      if (error instanceof ApiError && error.status === 409) {
        void queryClient.invalidateQueries({ queryKey: rulesQuery.queryKey });
      }
    },
  });

  const giving = rulesGiving(rules, id);
  const count = giving.length;
  const list = ruleNames(giving);
  const blocker =
    count === 0
      ? null
      : plural(count, {
          one: `${list} gives this machine role. Take it out of that rule, then delete the role.`,
          other: `${list} give this machine role. Take it out of those rules, then delete the role.`,
        });
  const refused = remove.error instanceof ApiError && remove.error.status === 409;

  return (
    <ConfirmDialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={t`Delete ${name}?`}
      confirmLabel={<Trans>Delete machine role</Trans>}
      danger
      isBusy={remove.isPending}
      isConfirmDisabled={blocker !== null}
      error={remove.isError && !(refused && blocker !== null) ? remove.error.message : undefined}
      onConfirm={() => {
        remove.mutate();
      }}
    >
      {blocker !== null ? (
        <>
          <p>{blocker}</p>
          <RuleLinks rules={giving} />
        </>
      ) : (
        <p>
          <Trans>No rule gives {name}, so deleting it changes the values of no machine.</Trans>
        </p>
      )}
    </ConfirmDialog>
  );
}
