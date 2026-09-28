// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { DeploymentStepView, RunInputView } from "@/deployments/deployments";
import { walk } from "@/sequences/flow/flowTree";
import type { SequenceDefinition } from "@/sequences/sequences";

import { usedValues, valueSourceText, type ResolvedValue } from "./values";

// One value as the machine's page lists it.
export interface ValueRow {
  name: string;
  // Null for a secret. The page only shows that it was given.
  value: string | null;
  source: string;
}

function sameName(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}

// The values a run works with and where each came from. That's the values it started with, plus the ones its steps
// set later, as the agent reported them. A step that set a value is named by its number and name.
export function valueRows({
  values,
  variables = null,
  definition = null,
  steps = [],
  inputs = null,
}: {
  values: readonly ResolvedValue[];
  variables?: Readonly<Record<string, string>> | null;
  definition?: SequenceDefinition | null;
  steps?: readonly DeploymentStepView[];
  inputs?: readonly RunInputView[] | null;
}): ValueRow[] {
  const entries = walk(definition?.steps ?? []);
  const done = new Set(steps.filter((step) => step.state === "Done").map((step) => step.stepId));
  const stepNamed = (id: string | null) => {
    const entry = id === null ? undefined : entries.find((candidate) => candidate.node.id === id);

    return entry === undefined ? null : { number: entry.number, name: entry.node.name };
  };
  // The last Set variable step that ran for the name. A script may have set it instead.
  const setterOf = (name: string) =>
    stepNamed(
      entries
        .filter(
          (entry) =>
            entry.node.kind === "setVariable" &&
            sameName(entry.node.variable, name) &&
            done.has(entry.node.id),
        )
        .at(-1)?.node.id ?? null,
    );
  const answeredBy = (name: string) =>
    inputs?.find((input) => sameName(input.input.name, name))?.answeredBy ?? null;
  const reported = Object.entries(variables ?? {});

  const rows = usedValues(values).map((value): ValueRow => {
    const set = reported.find(([name]) => sameName(name, value.name));

    if (set !== undefined && set[1] !== value.value && value.value !== null) {
      return {
        name: value.name,
        value: set[1],
        source: valueSourceText(
          { source: "Step", sourceName: null },
          { step: setterOf(value.name) },
        ),
      };
    }

    return {
      name: value.name,
      value: value.value,
      source: valueSourceText(value, {
        answeredBy: answeredBy(value.name),
        step: value.source === "Step" ? stepNamed(value.sourceId) : null,
      }),
    };
  });

  const added = reported
    .filter(([name]) => !rows.some((row) => sameName(row.name, name)))
    .map(([name, value]) => ({
      name,
      value,
      source: valueSourceText({ source: "Step", sourceName: null }, { step: setterOf(name) }),
    }));

  return [...rows, ...added];
}
