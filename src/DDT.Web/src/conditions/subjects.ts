// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react";
import { queryOptions, useQuery } from "@tanstack/react-query";
import { useMemo } from "react";

import { apiGet } from "@/lib/api";
import { rulesQuery } from "@/rules/rules";
import type { FactView, InputDeclaration, VariableDeclaration } from "@/sequences/sequences";

import { factCatalogue, subjectsOf, type Subject } from "./conditions";

// What conditions and templates can name, read from the server where it lists it: its catalogue of facts, and the
// values its rules and machine roles set. A server that lists none of them yet leaves the builder with the catalogue
// this build knows and no values.

export const factsQuery = queryOptions({
  queryKey: ["sequence-facts"],
  queryFn: () => apiGet<FactView[]>("/api/sequences/facts"),
  staleTime: Number.POSITIVE_INFINITY,
});

// A value a rule or a machine role sets, such as TimeZone = W. Europe Standard Time.
export interface NamedValue {
  name: string;
  value: string;
}

// Rules and machine roles as far as the builder reads them: the values they set, where the server sends them.
interface SetsValues {
  values?: NamedValue[] | null;
}

export const machineRolesQuery = queryOptions({
  queryKey: ["machine-roles"],
  queryFn: () => apiGet<SetsValues[]>("/api/machine-roles"),
  staleTime: 5 * 60_000,
});

function valuesOf(list: readonly unknown[] | undefined): NamedValue[] {
  return (list ?? []).flatMap((item) => (item as SetsValues).values ?? []);
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
  const roles = useQuery(machineRolesQuery);
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
        facts: facts.data ?? factCatalogue,
        valueNames: ruleValues.map((value) => value.name),
        variables,
        inputs,
      }),
      ruleValues,
      locale,
    };
  }, [facts.data, rules.data, roles.data, variables, inputs, locale]);
}
