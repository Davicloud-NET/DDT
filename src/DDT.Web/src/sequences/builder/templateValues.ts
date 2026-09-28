// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { sameName } from "@/conditions/conditionSubjects";
import { factCatalogue, sampleMachine } from "@/conditions/factCatalogue";
import type { NamedValue } from "@/conditions/subjects";

import { lookup, renderTemplate } from "../flow/templates";
import type { InputDeclaration, VariableDeclaration } from "../sequences";

// Every name a template can use, each once, in the order completion offers them: the sequence's own first, then the
// values of rules and roles, then the facts. An Account input sets no value.
export function templateNames(
  variables: readonly VariableDeclaration[],
  inputs: readonly InputDeclaration[],
  ruleValues: readonly NamedValue[],
): string[] {
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

  return names;
}

// A name's value on the sample machine, with the sequence's defaults and the values of rules and roles.
export function sampleValues(
  variables: readonly VariableDeclaration[],
  inputs: readonly InputDeclaration[],
  ruleValues: readonly NamedValue[],
): (name: string) => string | null {
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

  return (name: string) => sample(name);
}
