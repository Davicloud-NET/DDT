// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { RuleView } from "./rules";

// The list in the order of ids, with each rule renumbered by its place. Ids that aren't in the list are left out.
// Rules the ids don't name keep their order after the rest.
export function inOrder(list: readonly RuleView[], ids: readonly string[]): RuleView[] {
  const named = ids.flatMap((id) => list.filter((rule) => rule.id === id));
  const rest = list.filter((rule) => !ids.includes(rule.id));

  return [...named, ...rest].map((rule, position) =>
    rule.position === position ? rule : { ...rule, position },
  );
}

// The order after the rule moves by offset places, such as -1 for up. Null if it can't go further.
export function movedBy(list: readonly RuleView[], id: string, offset: number): string[] | null {
  const ids = list.map((rule) => rule.id);
  const from = ids.indexOf(id);
  const to = from + offset;

  if (from < 0 || to < 0 || to >= ids.length) {
    return null;
  }

  ids.splice(from, 1);
  ids.splice(to, 0, id);

  return ids;
}

// The order after the rules were dropped before or after another. Null if nothing moves.
export function droppedAt(
  list: readonly RuleView[],
  moving: readonly string[],
  target: string,
  position: "before" | "after",
): string[] | null {
  const ids = list.map((rule) => rule.id);
  const staying = ids.filter((id) => !moving.includes(id));
  const at = staying.indexOf(target);

  if (at < 0) {
    return null;
  }

  const cut = position === "before" ? at : at + 1;
  const order = [
    ...staying.slice(0, cut),
    ...moving.filter((id) => ids.includes(id)),
    ...staying.slice(cut),
  ];

  return order.every((id, index) => id === ids[index]) ? null : order;
}
