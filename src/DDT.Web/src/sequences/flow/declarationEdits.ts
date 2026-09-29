// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceDraft } from "../sequenceDraft";
import { renameReferences, sameName } from "./references";
import { insertAt } from "./treeChanges";

// A name of a variable or an input, as the server allows it.
export const namePattern = /^[A-Za-z][A-Za-z0-9_]{0,63}$/;

export function moveIn<T>(list: readonly T[], from: number, to: number): T[] {
  const item = list[from];

  if (item === undefined) {
    return [...list];
  }

  const rest = list.filter((_, index) => index !== from);

  return insertAt(rest, Math.max(0, Math.min(to, rest.length)), [item]);
}

export function named(list: readonly { name: string }[], name: string): number {
  return list.findIndex((item) => sameName(item.name, name));
}

export function renamed(draft: SequenceDraft, from: string, to: string): SequenceDraft {
  const variable = named(draft.variables, from);
  const input = named(draft.inputs, from);
  const taken = (list: readonly { name: string }[], own: number) =>
    list.some((item, index) => index !== own && sameName(item.name, to));

  if (
    from === to ||
    !namePattern.test(to) ||
    (variable < 0 && input < 0) ||
    taken(draft.variables, variable) ||
    taken(draft.inputs, input)
  ) {
    return draft;
  }

  const references = renameReferences(draft, from, to);

  return {
    ...references,
    variables: references.variables.map((item, index) =>
      index === variable ? { ...item, name: to } : item,
    ),
    inputs: references.inputs.map((item, index) =>
      index === input ? { ...item, name: to } : item,
    ),
  };
}
