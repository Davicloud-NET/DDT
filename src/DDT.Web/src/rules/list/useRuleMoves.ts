// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { inOrder, movedBy } from "../ruleOrder";
import {
  reorderRules,
  RulesChangedMeanwhile,
  rulesQuery,
  sequenceResolutionsKey,
  type RuleView,
} from "../rules";

// Why the last move did not happen as it was made: someone changed the rules meanwhile, or it failed.
export interface MoveProblem {
  text: string;
  tone: "attention" | "fail";
}

// Moves of rules. Each sends the whole order, and the list shows it at once and takes the server's answer.
export function useRuleMoves(list: readonly RuleView[]) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const [problem, setProblem] = useState<MoveProblem | null>(null);

  const move = useMutation({
    mutationFn: (order: string[]) => reorderRules(order),
    onMutate: async (order) => {
      await queryClient.cancelQueries({ queryKey: rulesQuery.queryKey });
      const before = queryClient.getQueryData(rulesQuery.queryKey);

      queryClient.setQueryData(rulesQuery.queryKey, (current) =>
        current === undefined ? current : inOrder(current, order),
      );
      setProblem(null);

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
        setProblem({
          text: t`Someone changed the rules while you moved one, so the list shows them as they are now. Move the rule again if it should still go there.`,
          tone: "attention",
        });
      } else {
        const message = error.message;

        setProblem({ text: t`The rule could not be moved: ${message}`, tone: "fail" });
      }
    },
  });

  return {
    problem,
    reorder: (order: string[]) => {
      move.mutate(order);
    },
    // By offset places, such as -1 for up; nothing where the rule cannot go further.
    moveBy: (id: string, offset: number) => {
      const order = movedBy(list, id, offset);

      if (order !== null) {
        move.mutate(order);
      }
    },
  };
}
