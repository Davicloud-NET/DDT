// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { restartsFailedNotice } from "@/machines/bootEnvironment";
import type { MachineSummary } from "@/machines/machines";
import { Notice } from "@/ui/Notice";

// Says that the server stopped restarting the machine for its assigned run, and what to look at.
export function RestartsFailedNotice({ machine }: { machine: MachineSummary }) {
  const notice = restartsFailedNotice(machine);

  return notice === null ? null : <Notice tone="attention">{notice}</Notice>;
}
