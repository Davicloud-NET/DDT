// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

// A run's values, such as ComputerName or TimeZone, and where each came from, as in the server's DDT.Contracts.Values.
// Also the values that rules and machine roles set. When a run starts, the server takes each name from the first
// source that sets it: input answers, the machine's own values, rules from the top, machine roles, the sequence's
// defaults, then the deployment defaults. The machine's page lists them with valueRows.ts.

// A value a rule or a machine role sets, such as TimeZone = W. Europe Standard Time. value is a template, such as
// PC-{{SerialNumber|alnum|right:12}}. Every signed-in user can read it, so it never holds a password.
export interface NamedValue {
  name: string;
  value: string;
}

export type ValueSource =
  "Input" | "Machine" | "Rule" | "Role" | "SequenceDefault" | "DeploymentDefault" | "Fact" | "Step";

// The server's ResolvedValue. sourceId and sourceName name the rule, machine role or step that set it, if any.
// overridden means a source higher up also set the name, so this value isn't used. value is null for a secret, so
// the page only shows that it's set.
export interface ResolvedValue {
  name: string;
  value: string | null;
  source: ValueSource;
  sourceId: string | null;
  sourceName: string | null;
  overridden: boolean;
}

// The text that says where a value came from, such as "From the rule Berlin office". answeredBy is who answered an
// input, if the page knows. step is the step that set the value during the run, with its number.
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

// The text an input's field shows about where its prefilled answer came from, such as "Filled in from the rule
// Berlin office.".
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

// A row in the value list of a rule's or machine role's drawer. Its key stays the same when rows above it are
// removed.
export interface EditedValue extends NamedValue {
  key: string;
}

export function editedValues(values: readonly NamedValue[]): EditedValue[] {
  return values.map((value) => ({ ...value, key: crypto.randomUUID() }));
}

function isBlank(row: NamedValue): boolean {
  return row.name.trim() === "" && row.value.trim() === "";
}

// The rows to send, without their keys. Rows with neither a name nor a value are left out.
export function namedValues(rows: readonly EditedValue[]): NamedValue[] {
  return rows
    .filter((row) => !isBlank(row))
    .map(({ name, value }) => ({ name: name.trim(), value }));
}

// Maps a field that was sent, such as values[1].name, back to the row it came from. Blank rows weren't sent, so each
// blank row before it moves it down by one.
export function rowField(rows: readonly EditedValue[], field: string): string {
  const match = /^values\[(\d+)\](.*)$/.exec(field);

  if (match === null) {
    return field;
  }

  const sent = rows.flatMap((row, index) => (isBlank(row) ? [] : [index]));
  const index = sent[Number(match[1])];

  return index === undefined ? field : `values[${String(index)}]${match[2] ?? ""}`;
}
