// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";

import { MAX_NETWORK_DETOURS } from "@/deployments/handOver";

interface LinuxDetoursProps {
  // How often in a row the machine started from the network while its run goes on in the installed Linux. Above 0.
  count: number;
}

// A machine that keeps starting from the network while its run goes on in the installed Linux. The server ends the
// run at the limit, so the page says the count before that, and not only the run's log.
export function LinuxDetours({ count }: LinuxDetoursProps) {
  const max = String(MAX_NETWORK_DETOURS);

  return (
    <p className="max-w-[80ch] type-small text-attention-text">
      {plural(count, {
        one: `The machine started from the network once and was sent back to its disk. At ${max} such starts in a row, the run ends.`,
        other: `The machine started from the network # times in a row and was sent back to its disk. At ${max} such starts in a row, the run ends.`,
      })}
    </p>
  );
}
