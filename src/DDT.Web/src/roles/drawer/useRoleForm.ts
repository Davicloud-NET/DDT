// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { ApiError } from "@/lib/api";
import { conflictOf, noFindings, otherRefusal, refusalFindings, unplaced } from "@/rules/refusals";
import { sequenceResolutionsKey } from "@/rules/rules";
import type { Findings } from "@/sequences/problems";
import { rowField } from "@/values/values";

import {
  createRole,
  putRole,
  roleEditOf,
  roleRequestOf,
  updateRole,
  type MachineRoleView,
  type RoleEdit,
} from "../roles";

function isShown(field: string): boolean {
  return /^(name|description|values)(\.|\[|$)/.test(field);
}

interface RoleFormOptions {
  // Null for a new role.
  role: MachineRoleView | null;
  onClose: () => void;
}

export type RoleForm = ReturnType<typeof useRoleForm>;

// A machine role's edit and its save. The server refuses values a run could not use, and says so at each value.
export function useRoleForm({ role, onClose }: RoleFormOptions) {
  const queryClient = useQueryClient();
  const [base, setBase] = useState<MachineRoleView | null>(role);
  const [edit, setEdit] = useState<RoleEdit>(() => roleEditOf(role));
  const [findings, setFindings] = useState<Findings>(noFindings);
  const [theirs, setTheirs] = useState<MachineRoleView | null>(null);
  const [gone, setGone] = useState(false);

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

  const takeTheirs = () => {
    if (theirs === null) {
      return;
    }

    setBase(theirs);
    setEdit(roleEditOf(theirs));
    setFindings(noFindings);
    setTheirs(null);
    save.reset();
  };

  const keepMine = () => {
    if (theirs === null) {
      return;
    }

    setBase(theirs);
    save.mutate(theirs.revision);
  };

  return {
    base,
    edit,
    findings,
    theirs,
    gone,
    change,
    busy: save.isPending,
    loose: unplaced(findings, isShown),
    refused: save.isError ? otherRefusal(save.error, gone) : null,
    submit: () => {
      save.mutate(base?.revision ?? 0);
    },
    takeTheirs,
    keepMine,
  };
}
