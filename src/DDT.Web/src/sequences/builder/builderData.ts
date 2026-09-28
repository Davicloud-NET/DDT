// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { createContext, useContext, useMemo } from "react";

import { accountsQuery, type AccountView } from "@/accounts/accounts";
import { factCatalogue, sampleMachine, type Subject } from "@/conditions/conditions";
import { useConditionData } from "@/conditions/subjects";

import { lookup, renderTemplate } from "../flow/templates";
import type { InputDeclaration, VariableDeclaration } from "../sequences";

// What the fields of the flow builder choose from and complete, beside the step catalog: the subjects of conditions,
// the names templates can use, a sample machine to preview them for, and the stored accounts.

export interface BuilderData {
  subjects: Subject[];
  // Every name a template can use, in the order completion offers them: the sequence's own first.
  names: string[];
  known: (name: string) => boolean;
  // A name's value on the sample machine, with the sequence's defaults and the values of rules and roles.
  sample: (name: string) => string | null;
  variables: readonly VariableDeclaration[];
  inputs: readonly InputDeclaration[];
  // Null while the server lists none, as before it has accounts.
  accounts: AccountView[] | null;
}

export const BuilderContext = createContext<BuilderData>({
  subjects: [],
  names: [],
  known: () => true,
  sample: () => null,
  variables: [],
  inputs: [],
  accounts: null,
});

export function useBuilder(): BuilderData {
  return useContext(BuilderContext);
}

function sameName(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}

// The builder's data for the sequence's declarations, read once for the page.
export function useBuilderData(declared: {
  variables: readonly VariableDeclaration[];
  inputs: readonly InputDeclaration[];
}): BuilderData {
  const { subjects, ruleValues } = useConditionData(declared);
  const accounts = useQuery({ ...accountsQuery, staleTime: 5 * 60_000 });
  const { variables, inputs } = declared;

  return useMemo(() => {
    const own = [
      ...variables.map((variable) => variable.name),
      ...inputs.filter((input) => input.kind !== "Account").map((input) => input.name),
    ];
    const names: string[] = [];

    for (const name of [
      ...own,
      ...ruleValues.map((value) => value.name),
      ...factCatalogue.map((fact) => fact.name),
    ]) {
      if (!names.some((other) => sameName(other, name))) {
        names.push(name);
      }
    }

    const facts = lookup(sampleMachine);
    const rules = lookup(Object.fromEntries(ruleValues.map((value) => [value.name, value.value])));

    // A default is a template itself; one that names itself, directly or through others, has no value.
    const sample = (name: string, seen: readonly string[] = []): string | null => {
      if (seen.some((other) => sameName(other, name))) {
        return null;
      }

      const fact = facts(name);

      if (fact !== null) {
        return fact;
      }

      const next = [...seen, name];
      const rendered = (template: string | null) =>
        template === null ? null : renderTemplate(template, (inner) => sample(inner, next)).output;
      const variable = variables.find((candidate) => sameName(candidate.name, name));
      const input = inputs.find((candidate) => sameName(candidate.name, name));

      return (
        rendered(input?.default ?? null) ??
        rendered(variable?.default ?? null) ??
        rendered(rules(name))
      );
    };

    return {
      subjects,
      names,
      known: (name: string) => names.some((other) => sameName(other, name)),
      sample: (name: string) => sample(name),
      variables,
      inputs,
      accounts: accounts.data ?? null,
    };
  }, [subjects, ruleValues, variables, inputs, accounts.data]);
}
