// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// The values rules and machine roles set, and the values a run has with where each came from, as the server's
// DDT.Contracts.Values has them.

// A value a rule or a machine role sets, such as TimeZone = W. Europe Standard Time. value is a template, such as
// PC-{{SerialNumber|alnum|right:12}}. Every signed-in user can read it, so it never holds a password.
export interface NamedValue {
  name: string;
  value: string;
}

// Where a value came from, first in the order first: an answer to an input, the machine's own value, a rule, a
// machine role a rule gave, the sequence's default, the deployment defaults; a fact of the machine, or a step.
export type ValueSource =
  "Input" | "Machine" | "Rule" | "Role" | "SequenceDefault" | "DeploymentDefault" | "Fact" | "Step";

// A value of a run or of a preview. sourceId and sourceName name the rule, machine role or step that set it, where
// one did. overridden marks a value a source further up the order also set, which is shown but not used. value is
// null for a secret, which shows only that it is set.
export interface ResolvedValue {
  name: string;
  value: string | null;
  source: ValueSource;
  sourceId: string | null;
  sourceName: string | null;
  overridden: boolean;
}

// A value of the list that a page edits, with a key that stays when rows above it are taken away.
export interface ValueRow extends NamedValue {
  key: string;
}

export function valueRows(values: readonly NamedValue[]): ValueRow[] {
  return values.map((value) => ({ ...value, key: crypto.randomUUID() }));
}

function isBlank(row: NamedValue): boolean {
  return row.name.trim() === "" && row.value.trim() === "";
}

// What is sent: the rows without their keys, a row with neither a name nor a value left out.
export function namedValues(rows: readonly ValueRow[]): NamedValue[] {
  return rows
    .filter((row) => !isBlank(row))
    .map(({ name, value }) => ({ name: name.trim(), value }));
}

// A field of what was sent, such as values[1].name, as the field of the row it came from: the blank rows left out
// before it move it down.
export function rowField(rows: readonly ValueRow[], field: string): string {
  const match = /^values\[(\d+)\](.*)$/.exec(field);

  if (match === null) {
    return field;
  }

  const sent = rows.flatMap((row, index) => (isBlank(row) ? [] : [index]));
  const index = sent[Number(match[1])];

  return index === undefined ? field : `values[${String(index)}]${match[2] ?? ""}`;
}
