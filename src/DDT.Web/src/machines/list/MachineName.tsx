// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Link } from "@tanstack/react-router";

import type { MachineSummary } from "@/machines/machines";
import { displayName, hardwareLine } from "@/machines/machineView";

// The machine's name, linked to its page. Below it is the hardware that the name leaves out.
export function MachineName({
  machine,
  className,
}: {
  machine: MachineSummary;
  className: string;
}) {
  return (
    <span className={className}>
      <Link
        to="/machines/$machineId"
        params={{ machineId: machine.id }}
        className="truncate type-label text-[16.5px] hover:underline"
      >
        {displayName(machine)}
      </Link>
      <span className="truncate type-small text-muted">{hardwareLine(machine)}</span>
    </span>
  );
}
