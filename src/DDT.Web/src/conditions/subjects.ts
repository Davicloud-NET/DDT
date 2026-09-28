// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react";
import { queryOptions, useQuery } from "@tanstack/react-query";
import { useMemo } from "react";

import { apiGet } from "@/lib/api";
import { machineRolesQuery } from "@/roles/roles";
import { rulesQuery } from "@/rules/rules";
import type { FactView, InputDeclaration, VariableDeclaration } from "@/sequences/sequences";
import type { NamedValue } from "@/values/values";

import { factCatalogue, subjectsOf, type Subject } from "./conditions";

// What conditions and templates can name, read from the server where it lists it: its catalogue of facts, and the
// values its rules and machine roles set. A server that lists none of them yet leaves the builder with the catalogue
// this build knows and no values.

export const factsQuery = queryOptions({
  queryKey: ["sequence-facts"],
  queryFn: () => apiGet<FactView[]>("/api/sequences/facts"),
  staleTime: Number.POSITIVE_INFINITY,
});

export type { NamedValue };

function valuesOf(list: readonly { values: NamedValue[] }[] | undefined): NamedValue[] {
  return (list ?? []).flatMap((item) => item.values);
}

export interface ConditionData {
  subjects: Subject[];
  // The values rules and machine roles set, each name once, the first rule's first.
  ruleValues: NamedValue[];
  // The language the labels are in.
  locale: string;
}

export function useConditionData(declared: {
  variables: readonly VariableDeclaration[];
  inputs: readonly InputDeclaration[];
}): ConditionData {
  const facts = useQuery(factsQuery);
  const rules = useQuery({ ...rulesQuery, staleTime: 5 * 60_000 });
  const roles = useQuery({ ...machineRolesQuery, staleTime: 5 * 60_000 });
  const { variables, inputs } = declared;
  // The labels are in the person's language.
  const locale = useLingui().i18n.locale;

  return useMemo(() => {
    const ruleValues: NamedValue[] = [];

    for (const value of [...valuesOf(rules.data), ...valuesOf(roles.data)]) {
      if (!ruleValues.some((other) => other.name.toLowerCase() === value.name.toLowerCase())) {
        ruleValues.push(value);
      }
    }

    return {
      subjects: subjectsOf({
        // A server that lists no facts yet leaves the catalogue this build knows.
        facts: facts.data !== undefined && facts.data.length > 0 ? facts.data : factCatalogue,
        valueNames: ruleValues.map((value) => value.name),
        variables,
        inputs,
      }),
      ruleValues,
      locale,
    };
  }, [facts.data, rules.data, roles.data, variables, inputs, locale]);
}
