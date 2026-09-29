// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import type { HardwareModel } from "@/machines/machines";

import { targetRows, type TargetRow } from "./packageTargets";

export type TargetRows = ReturnType<typeof useTargetRows>;

// The hardware model rows of the package dialog, which may be added, changed and removed.
export function useTargetRows(initial: readonly HardwareModel[]) {
  const [rows, setRows] = useState<TargetRow[]>(() => targetRows(initial));
  const [nextKey, setNextKey] = useState(initial.length);

  return {
    rows,
    change: (key: number, change: Partial<Omit<TargetRow, "key">>) => {
      setRows((current) => current.map((row) => (row.key === key ? { ...row, ...change } : row)));
    },
    remove: (key: number) => {
      setRows((current) => current.filter((row) => row.key !== key));
    },
    add: () => {
      setRows((current) => [...current, { key: nextKey, manufacturer: "", model: "" }]);
      setNextKey((key) => key + 1);
    },
  };
}
