// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { ApiError } from "@/lib/api";

// The machine a removal asks about: it has recovery keys, which go with it.
export interface RemoveKeysRequest {
  machineId: string;
  count: number;
}

// The number of keys the server wants confirmed before it removes the machine, or null for any other refusal.
export function recoveryKeysToConfirm(error: unknown): number | null {
  if (!(error instanceof ApiError) || error.problem?.code !== "machine.removeRecoveryKeys") {
    return null;
  }

  const count = error.problem.args?.count;

  return typeof count === "number" ? count : null;
}
