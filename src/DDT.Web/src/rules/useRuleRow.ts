// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";

import { equalJson } from "@/lib/equalJson";
import { useAutosave } from "@/lib/useAutosave";

import {
  editOf,
  refusalMessages,
  requestOf,
  rulesQuery,
  updateRule,
  type AssignmentRuleView,
  type RuleEdit,
  type RuleField,
} from "./rules";

// One rule, saved as it is edited. Rules have no revision, so the last save wins, as on the server.
export function useRuleRow(rule: AssignmentRuleView) {
  const queryClient = useQueryClient();

  const autosave = useAutosave<RuleEdit, AssignmentRuleView>({
    initial: { value: editOf(rule), revision: 0 },
    save: (edit) => updateRule(rule.id, requestOf(rule.kind, edit)),
    savedAs: (saved) => ({ value: editOf(saved), revision: 0 }),
    equals: equalJson,
    onSaved: (saved) => {
      queryClient.setQueryData(rulesQuery.queryKey, (list) =>
        list?.map((existing) => (existing.id === saved.id ? saved : existing)),
      );
    },
  });

  const { receive, update, state } = autosave;

  // Another administrator's change shows at once while this row has nothing unsaved.
  useEffect(() => {
    receive(editOf(rule), 0);
  }, [receive, rule]);

  return {
    edit: autosave.value,
    state,
    change: (patch: Partial<RuleEdit>, immediate = false) => {
      update((edit) => ({ ...edit, ...patch }), immediate);
    },
    messages: (field: RuleField) =>
      refusalMessages(rule.kind, state.kind === "refused" ? state : null, field),
  };
}
