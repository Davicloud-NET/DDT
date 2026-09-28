// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Updaters of a cached list of items with ids, for setQueryData. A list never read stays undefined, so a page that
// needs it reads it whole.

// The list with item in place of its older copy, or added, sorted by compare. item goes first, so it leads the items
// compare finds equal to it.
export function upsertById<T extends { id: string }>(
  list: readonly T[] | undefined,
  item: T,
  compare: (a: T, b: T) => number,
): T[] | undefined {
  return list === undefined
    ? undefined
    : [item, ...list.filter((existing) => existing.id !== item.id)].sort(compare);
}

export function removeByIds<T extends { id: string }>(
  list: readonly T[] | undefined,
  ids: readonly string[],
): T[] | undefined {
  const removed = new Set(ids);

  return list?.filter((item) => !removed.has(item.id));
}
