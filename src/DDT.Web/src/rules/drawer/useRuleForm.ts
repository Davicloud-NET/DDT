// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { ApiError } from "@/lib/api";
import type { Findings } from "@/sequences/problems";
import { rowField } from "@/values/values";

import { conflictOf, noFindings, otherRefusal, refusalFindings, unplaced } from "../refusals";
import { editOf, emptyEdit, requestOf, type RuleEdit } from "../ruleEdit";
import { createRule, putRule, sequenceResolutionsKey, updateRule, type RuleView } from "../rules";

// The fields the drawer shows a finding at; the rest go in a notice at its top.
function isShown(field: string): boolean {
  return /^(name|description|sequenceId|enabled|when|values|roleIds)(\.|\[|$)/.test(field);
}

interface RuleFormOptions {
  // Null for a new rule.
  rule: RuleView | null;
  // A new rule saved with problems stays open as the rule it now is.
  onSaved: (rule: RuleView) => void;
  onClose: () => void;
}

export type RuleForm = ReturnType<typeof useRuleForm>;

// A rule's edit and its save. The server saves a rule even with problems, which keep it from matching until they are
// fixed, so the drawer then stays open with each problem at its field.
export function useRuleForm({ rule, onSaved, onClose }: RuleFormOptions) {
  const queryClient = useQueryClient();
  // The rule as last read or saved here, whose revision a save names.
  const [base, setBase] = useState<RuleView | null>(rule);
  const [edit, setEdit] = useState<RuleEdit>(() => (rule === null ? emptyEdit() : editOf(rule)));
  const [findings, setFindings] = useState<Findings>(() =>
    rule === null ? noFindings : { problems: rule.problems, warnings: [] },
  );
  const [theirs, setTheirs] = useState<RuleView | null>(null);
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

  const takeTheirs = () => {
    if (theirs === null) {
      return;
    }

    setBase(theirs);
    setEdit(editOf(theirs));
    setFindings({ problems: theirs.problems, warnings: [] });
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
