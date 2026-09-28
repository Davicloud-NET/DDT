// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

// The values a run works with, such as ComputerName or TimeZone, and where each came from, as the server's
// DDT.Contracts.Values has them; and the values rules and machine roles set, as their pages edit them. The server works a
// run's values out when it starts, from the first source that sets a name: input answers, the machine's own values,
// rules from the top, machine roles, the sequence's defaults and the deployment defaults. The machine's page lists
// them with valueRows.ts.

// A value a rule or a machine role sets, such as TimeZone = W. Europe Standard Time. value is a template, such as
// PC-{{SerialNumber|alnum|right:12}}. Every signed-in user can read it, so it never holds a password.
export interface NamedValue {
  name: string;
  value: string;
}

export type ValueSource =
  "Input" | "Machine" | "Rule" | "Role" | "SequenceDefault" | "DeploymentDefault" | "Fact" | "Step";

// The server's ResolvedValue. sourceId and sourceName name the rule, machine role or step that set it, where one did.
// overridden marks a value a source further up also set, which is not used. value is null for a secret, which shows
// only that it is set.
export interface ResolvedValue {
  name: string;
  value: string | null;
  source: ValueSource;
  sourceId: string | null;
  sourceName: string | null;
  overridden: boolean;
}

// What a value's line says of its source, such as "From the rule Berlin office". answeredBy is who answered an input,
// where the page knows it; step is the step that set a value while the run went on, with its number.
export function valueSourceText(
  value: Pick<ResolvedValue, "source" | "sourceName">,
  more: { answeredBy?: string | null; step?: { number: number | null; name: string } | null } = {},
): string {
  const name = value.sourceName;

  switch (value.source) {
    case "Input": {
      const by = more.answeredBy ?? null;

      return by === null ? t`Answered for this run` : t`Answered by ${by} for this run`;
    }
    case "Machine":
      return t`Set on this machine`;
    case "Rule":
      return name === null ? t`From a rule` : t`From the rule ${name}`;
    case "Role":
      return name === null ? t`From a machine role` : t`From the machine role ${name}`;
    case "SequenceDefault":
      return t`The sequence's default`;
    case "DeploymentDefault":
      return t`From the deployment defaults`;
    case "Fact":
      return t`Reported by the machine`;
    case "Step": {
      const step = more.step ?? null;

      if (step === null) {
        return name === null ? t`Set by a step while the run went on` : t`Set by the step ${name}`;
      }

      const stepName = step.name;
      const number = step.number;

      return number === null
        ? t`Set by the step ${stepName}`
        : t`Set by step ${number}, ${stepName}`;
    }
  }
}

// What an input's field says of the answer it starts with, such as "Filled in from the rule Berlin office.".
export function prefillText(value: Pick<ResolvedValue, "source" | "sourceName">): string {
  const name = value.sourceName;

  switch (value.source) {
    case "Input":
      return t`Filled in with the answer given before.`;
    case "Machine":
      return t`Filled in with this machine's own value.`;
    case "Rule":
      return name === null ? t`Filled in by a rule.` : t`Filled in from the rule ${name}.`;
    case "Role":
      return name === null
        ? t`Filled in by a machine role.`
        : t`Filled in from the machine role ${name}.`;
    case "SequenceDefault":
      return t`Filled in with the sequence's default.`;
    case "DeploymentDefault":
      return t`Filled in from the deployment defaults.`;
    case "Fact":
      return t`Filled in with what the machine reported.`;
    case "Step":
      return t`Filled in with a value a step set.`;
  }
}

// The values that are used, one per name, in the order the server gave them.
export function usedValues(values: readonly ResolvedValue[]): ResolvedValue[] {
  return values.filter((value) => !value.overridden);
}

// A value of the list that a rule's or a machine role's drawer edits, with a key that stays when rows above it are
// taken away.
export interface EditedValue extends NamedValue {
  key: string;
}

export function editedValues(values: readonly NamedValue[]): EditedValue[] {
  return values.map((value) => ({ ...value, key: crypto.randomUUID() }));
}

function isBlank(row: NamedValue): boolean {
  return row.name.trim() === "" && row.value.trim() === "";
}

// What is sent: the rows without their keys, a row with neither a name nor a value left out.
export function namedValues(rows: readonly EditedValue[]): NamedValue[] {
  return rows
    .filter((row) => !isBlank(row))
    .map(({ name, value }) => ({ name: name.trim(), value }));
}

// A field of what was sent, such as values[1].name, as the field of the row it came from: the blank rows left out
// before it move it down.
export function rowField(rows: readonly EditedValue[], field: string): string {
  const match = /^values\[(\d+)\](.*)$/.exec(field);

  if (match === null) {
    return field;
  }

  const sent = rows.flatMap((row, index) => (isBlank(row) ? [] : [index]));
  const index = sent[Number(match[1])];

  return index === undefined ? field : `values[${String(index)}]${match[2] ?? ""}`;
}
