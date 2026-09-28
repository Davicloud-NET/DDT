// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// A rail's grid: one column per step, all as wide.
export function railColumns(count: number) {
  return { gridTemplateColumns: `repeat(${String(Math.max(count, 1))}, minmax(0, 1fr))` };
}
