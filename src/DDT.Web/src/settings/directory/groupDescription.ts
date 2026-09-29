// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// What shows next to a group in the map: its name, a note that the directory doesn't have it, or nothing.
export type GroupDescription = { name: string } | "notFound" | null;

// A group added from the search shows the name the search found. For the saved map, the directory's answer
// says which saved groups it doesn't have. Both maps are keyed by the lower-case distinguished name.
export function describeGroup(
  key: string,
  found: ReadonlyMap<string, string | null>,
  directoryNames: ReadonlyMap<string, string | null>,
): GroupDescription {
  const lower = key.trim().toLowerCase();

  if (found.has(lower) || !directoryNames.has(lower)) {
    const name = found.get(lower) ?? null;

    return name === null ? null : { name };
  }

  const name = directoryNames.get(lower) ?? null;

  return name === null ? "notFound" : { name };
}
