// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// The most names the completion offers at once.
const OFFERED = 8;

// The names that start with what is typed, then those that hold it further on, ignoring case.
export function offeredNames(names: readonly string[], typed: string): string[] {
  const lower = typed.toLowerCase();

  return [
    ...names.filter((name) => name.toLowerCase().startsWith(lower)),
    ...names.filter(
      (name) => !name.toLowerCase().startsWith(lower) && name.toLowerCase().includes(lower),
    ),
  ].slice(0, OFFERED);
}
