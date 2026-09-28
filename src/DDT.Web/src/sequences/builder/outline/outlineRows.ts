// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { Key } from "react-aria-components";

import type { SequenceStep } from "../../sequences";
import { isContainer } from "../../steps";

// A row of the outline: a node, or the Then or Else of an IF, which hold its branches. path numbers each row by its
// place, such as 2.1.3 for the third node in the Then of the second node.
export interface OutlineRow {
  id: string;
  kind: "node" | "branch";
  node: SequenceStep;
  branch: "then" | "else" | null;
  path: string;
  children: OutlineRow[];
}

// Joins an IF's id and its branch in the key of a Then or an Else row.
export const BRANCH_SEPARATOR = "|";

export function rowsOf(list: readonly SequenceStep[], prefix: string): OutlineRow[] {
  return list.map((node, index) => {
    const path = `${prefix}${String(index + 1)}`;
    const children: OutlineRow[] =
      node.kind === "if"
        ? (["then", "else"] as const).map((branch, position) => ({
            id: `${node.id}${BRANCH_SEPARATOR}${branch}`,
            kind: "branch",
            node,
            branch,
            path: `${path}.${String(position + 1)}`,
            children: rowsOf(node[branch], `${path}.${String(position + 1)}.`),
          }))
        : node.kind === "group" || node.kind === "repeat"
          ? rowsOf(node.steps, `${path}.`)
          : [];

    return { id: node.id, kind: "node", node, branch: null, path, children };
  });
}

// The rows that open and close: the ones with rows inside, the branches, and every container, empty ones too.
export function expandableKeys(rows: readonly OutlineRow[]): Key[] {
  const expandable: Key[] = [];
  const collect = (list: readonly OutlineRow[]) => {
    for (const row of list) {
      if (row.children.length > 0 || row.kind === "branch" || isContainer(row.node)) {
        expandable.push(row.id);
      }

      collect(row.children);
    }
  };

  collect(rows);

  return expandable;
}
