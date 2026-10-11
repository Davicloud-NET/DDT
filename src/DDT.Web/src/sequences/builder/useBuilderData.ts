// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";

import { accountsQuery } from "@/accounts/accounts";
import { sameName } from "@/conditions/conditionSubjects";
import { useConditionData } from "@/conditions/subjects";

import { sequenceKind } from "../sequenceKinds";
import type { InputDeclaration, SequenceStep, VariableDeclaration } from "../sequences";
import type { BuilderData } from "./builderData";
import { sampleValues, templateNames } from "./templateValues";

// The builder's data for the sequence's declarations, read once for the page. A rule or a machine role has no steps.
export function useBuilderData(declared: {
  variables: readonly VariableDeclaration[];
  inputs: readonly InputDeclaration[];
  steps?: readonly SequenceStep[];
}): BuilderData {
  const { variables, inputs, steps } = declared;
  const kind = useMemo(() => (steps === undefined ? undefined : sequenceKind(steps)), [steps]);
  const { subjects, ruleValues } = useConditionData({ variables, inputs, kind });
  const accounts = useQuery({ ...accountsQuery, staleTime: 5 * 60_000 });

  return useMemo(() => {
    const names = templateNames(variables, inputs, ruleValues);

    return {
      subjects,
      names,
      known: (name: string) => names.some((other) => sameName(other, name)),
      sample: sampleValues(variables, inputs, ruleValues),
      variables,
      inputs,
      accounts: accounts.data ?? null,
    };
  }, [subjects, ruleValues, variables, inputs, accounts.data]);
}
