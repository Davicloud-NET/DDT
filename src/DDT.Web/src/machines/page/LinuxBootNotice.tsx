// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";

import { deploymentOptionsQuery } from "@/deployments/deployments";
import type { MachineSummary } from "@/machines/machines";
import { linuxBootRefusal } from "@/machines/secureBoot";
import { Notice } from "@/ui/Notice";

// Says why netboot does not start this machine in the Linux boot environment, when its firmware would refuse it.
// Netboot believes only what a machine someone approved reported, since anyone can register a machine.
export function LinuxBootNotice({ machine }: { machine: MachineSummary }) {
  const options = useQuery(deploymentOptionsQuery).data;
  const refusal = machine.everApproved
    ? linuxBootRefusal(machine, options?.linuxShimSignedUnder, options?.bootEnvironments)
    : null;

  return refusal === null ? null : <Notice tone="info">{refusal}</Notice>;
}
