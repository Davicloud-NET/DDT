// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";

import { accountsQuery } from "@/accounts/accounts";
import { sameName } from "@/conditions/conditionSubjects";
import { useConditionData } from "@/conditions/subjects";

import type { InputDeclaration, VariableDeclaration } from "../sequences";
import type { BuilderData } from "./builderData";
import { sampleValues, templateNames } from "./templateValues";

// The builder's data for the sequence's declarations, read once for the page.
export function useBuilderData(declared: {
  variables: readonly VariableDeclaration[];
  inputs: readonly InputDeclaration[];
}): BuilderData {
  const { subjects, ruleValues } = useConditionData(declared);
  const accounts = useQuery({ ...accountsQuery, staleTime: 5 * 60_000 });
  const { variables, inputs } = declared;

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
