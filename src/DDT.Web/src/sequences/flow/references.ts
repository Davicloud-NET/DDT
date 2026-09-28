// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceDraft } from "../sequenceDraft";
import type { AccountReference, SequenceStep, StepCondition, StepKind } from "../sequences";
import {
  conditionOf,
  conditionPath,
  mapTests,
  testsOf,
  type ConditionField,
} from "./conditionTree";
import { bodiesOf, walk } from "./flowTree";

// Where a sequence names its variables and inputs: {{Name}} in the text fields that are templates, the variable of
// a condition's test or of a Set variable step, and an Account input in an account reference. Names ignore case, as
// the server's templates and conditions do.

// The text fields of each kind that are templates. A script's text is not one: scripts get the values in their
// environment.
export const templateFields: Partial<Record<StepKind, readonly string[]>> = {
  writeUnattend: ["timeZone", "locale", "keyboard"],
  joinDomain: ["organizationalUnit"],
  writeCloudInitSeed: ["metaData", "userData", "networkConfig"],
  setVariable: ["value"],
  pause: ["message"],
};

// {{Name}} and {{Name|filter|filter:n}}, with or without spaces inside the braces.
const placeholder = /\{\{(\s*)([A-Za-z][A-Za-z0-9_]*)(\s*(?:\|[^{}]*)?)\}\}/g;

// Names are letters, digits and underscores, so comparing them in lower case ignores case as the server does.
export function sameName(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}

// The names a template uses, in order, each once.
export function templateNames(text: string): string[] {
  const names: string[] = [];

  for (const match of text.matchAll(placeholder)) {
    const name = match[2] ?? "";

    if (!names.some((known) => sameName(known, name))) {
      names.push(name);
    }
  }

  return names;
}

export function renameInTemplate(text: string, from: string, to: string): string {
  return text.replace(placeholder, (whole, before: string, name: string, after: string) =>
    sameName(name, from) ? `{{${before}${to}${after}}}` : whole,
  );
}

// A place that names a variable or an input: a field of a node, as a problem names it, or of the sequence's own
// declarations when nodeId is null, such as "variables[1].default".
export interface Reference {
  nodeId: string | null;
  field: string;
}

function textOf(node: SequenceStep, field: string): string | null {
  const value: unknown = (node as unknown as Record<string, unknown>)[field];

  return typeof value === "string" ? value : null;
}

function names(reference: AccountReference | null | undefined, name: string): boolean {
  return (
    reference?.input !== null && reference?.input !== undefined && sameName(reference.input, name)
  );
}

// Where the sequence names name, in document order: its nodes first, then the defaults of its variables.
export function referencesTo(draft: SequenceDraft, name: string): Reference[] {
  const found: Reference[] = [];

  for (const { node } of walk(draft.steps)) {
    const at = (field: string) => {
      found.push({ nodeId: node.id, field });
    };

    node.conditions.forEach((condition, index) => {
      if (sameName(condition.variable, name)) {
        at(`conditions[${String(index)}]`);
      }
    });

    for (const field of ["when", "test", "until"] as const satisfies ConditionField[]) {
      for (const { path, test } of testsOf(conditionOf(node, field))) {
        if (sameName(test.variable, name)) {
          at(conditionPath(field, path));
        }
      }
    }

    if (node.kind === "setVariable" && sameName(node.variable, name)) {
      at("variable");
    }

    for (const field of templateFields[node.kind] ?? []) {
      const text = textOf(node, field);

      if (text !== null && templateNames(text).some((used) => sameName(used, name))) {
        at(field);
      }
    }

    (node.shares ?? []).forEach((share, index) => {
      if (templateNames(share.path).some((used) => sameName(used, name))) {
        at(`shares[${String(index)}].path`);
      }

      if (names(share.account, name)) {
        at(`shares[${String(index)}].account`);
      }
    });

    if (node.kind === "runScript" && names(node.runAs, name)) {
      at("runAs");
    }

    if (node.kind === "joinDomain" && names(node.account, name)) {
      at("account");
    }
  }

  draft.variables.forEach((variable, index) => {
    if (
      variable.default !== null &&
      templateNames(variable.default).some((used) => sameName(used, name))
    ) {
      found.push({ nodeId: null, field: `variables[${String(index)}].default` });
    }
  });

  return found;
}

// The nodes that name name, each once, in document order: what a variable or an input is "used by".
export function usedBy(draft: SequenceDraft, name: string): string[] {
  const ids: string[] = [];

  for (const reference of referencesTo(draft, name)) {
    if (reference.nodeId !== null && !ids.includes(reference.nodeId)) {
      ids.push(reference.nodeId);
    }
  }

  return ids;
}

function renamedReference(
  reference: AccountReference | null | undefined,
  from: string,
  to: string,
): AccountReference | null | undefined {
  return reference && names(reference, from) ? { ...reference, input: to } : reference;
}

function renamedConditions(conditions: StepCondition[], from: string, to: string): StepCondition[] {
  return conditions.some((condition) => sameName(condition.variable, from))
    ? conditions.map((condition) =>
        sameName(condition.variable, from) ? { ...condition, variable: to } : condition,
      )
    : conditions;
}

function renamedNode(node: SequenceStep, from: string, to: string): SequenceStep {
  const renameTest = <T extends { variable: string }>(test: T): T =>
    sameName(test.variable, from) ? { ...test, variable: to } : test;
  const record = { ...node } as unknown as Record<string, unknown>;

  record.conditions = renamedConditions(node.conditions, from, to);

  if (node.when) {
    record.when = mapTests(node.when, renameTest);
  }

  if (node.kind === "if") {
    record.test = mapTests(node.test, renameTest);
  }

  if (node.kind === "repeat") {
    record.until = mapTests(node.until, renameTest);
  }

  for (const field of templateFields[node.kind] ?? []) {
    const text = textOf(node, field);

    if (text !== null) {
      record[field] = renameInTemplate(text, from, to);
    }
  }

  if (node.kind === "setVariable" && sameName(node.variable, from)) {
    record.variable = to;
  }

  if (node.shares) {
    record.shares = node.shares.map((share) => ({
      path: renameInTemplate(share.path, from, to),
      account: renamedReference(share.account, from, to) ?? share.account,
    }));
  }

  if (node.kind === "runScript" && node.runAs) {
    record.runAs = renamedReference(node.runAs, from, to);
  }

  if (node.kind === "joinDomain" && node.account) {
    record.account = renamedReference(node.account, from, to);
  }

  return record as unknown as SequenceStep;
}

function renamedSteps(steps: SequenceStep[], from: string, to: string): SequenceStep[] {
  return steps.map((node) => {
    let renamed = renamedNode(node, from, to);

    for (const body of bodiesOf(node)) {
      const inside = renamedSteps(body.steps, from, to);

      renamed = { ...renamed, [body.name]: inside };
    }

    return renamed;
  });
}

// Every place that names from names to instead: templates, conditions, Set variable steps, account references and
// the defaults of the variables. The declarations themselves are the caller's.
export function renameReferences(draft: SequenceDraft, from: string, to: string): SequenceDraft {
  return {
    ...draft,
    steps: renamedSteps(draft.steps, from, to),
    variables: draft.variables.map((variable) =>
      variable.default === null
        ? variable
        : { ...variable, default: renameInTemplate(variable.default, from, to) },
    ),
  };
}
