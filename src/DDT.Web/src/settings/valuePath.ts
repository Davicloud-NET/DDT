// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// A dotted path such as "domain.name" into a section's values.
export function valueAt(values: unknown, path: string): unknown {
  return path
    .split(".")
    .reduce<unknown>(
      (current, key) =>
        current !== null && typeof current === "object"
          ? (current as Record<string, unknown>)[key]
          : undefined,
      values,
    );
}

export function withValueAt<T>(values: T, path: string, value: unknown): T {
  const [head, ...rest] = path.split(".");
  const record = (values ?? {}) as Record<string, unknown>;

  if (head === undefined) {
    return values;
  }

  return {
    ...record,
    [head]: rest.length === 0 ? value : withValueAt(record[head], rest.join("."), value),
  } as T;
}
