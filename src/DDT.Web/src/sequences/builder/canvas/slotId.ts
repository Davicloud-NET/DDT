// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { Slot } from "../../flow/flowTree";

// A slot as a string, for React's key and for finding the slot's element again.
export function slotId(slot: Slot): string {
  return `${slot.parent ?? ""}/${slot.body}/${String(slot.index)}`;
}
