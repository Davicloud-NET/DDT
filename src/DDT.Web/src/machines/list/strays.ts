// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { isStray, type MachineSummary } from "@/machines/machines";

// The strays that registered from one address, which the first of them offers to remove at once.
export interface StrayOffer {
  address: string;
  count: number;
}

// A machine that registered and waits without anyone having approved it may be a stray. Where more than one came
// from the same address, the first of them offers to remove them all.
export function straysOffer(machines: readonly MachineSummary[]): Map<string, StrayOffer> {
  const byAddress = new Map<string, MachineSummary[]>();

  for (const machine of machines) {
    if (isStray(machine) && machine.firstSeenAddress !== null) {
      byAddress.set(machine.firstSeenAddress, [
        ...(byAddress.get(machine.firstSeenAddress) ?? []),
        machine,
      ]);
    }
  }

  const offers = new Map<string, StrayOffer>();

  for (const [address, group] of byAddress) {
    const first = group[0];

    if (group.length > 1 && first !== undefined) {
      offers.set(first.id, { address, count: group.length });
    }
  }

  return offers;
}
