// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { relativeTime } from "@/lib/relativeTime";
import { formatMac, type MachineSummary } from "@/machines/machines";
import { secureBootFact } from "@/machines/secureBoot";
import { Facts } from "@/ui/Facts";

// The plate under the machine's name that shows what identifies the machine.
export function MachinePlate({ machine, now }: { machine: MachineSummary; now: number }) {
  const seen = relativeTime(machine.lastSeenUtc, now);
  const from = machine.lastSeenAddress;
  const disks =
    machine.disks ??
    (machine.eligibleDiskCount === 0 ? t`No disk DDT can install on` : t`Not reported`);

  return (
    <Facts
      layout="plate"
      items={[
        {
          label: <Trans>Serial</Trans>,
          value: machine.serialNumber ?? t`Not reported`,
          mono: true,
        },
        {
          label: <Trans>MAC address</Trans>,
          value: machine.macAddresses.map(formatMac).join(", "),
          mono: true,
        },
        {
          label: <Trans>Last seen</Trans>,
          value: from === null ? seen : t`${seen} from ${from}`,
        },
        { label: <Trans>Secure Boot</Trans>, value: secureBootFact(machine) },
        { label: <Trans>Disks</Trans>, value: disks },
        { label: <Trans>Agent</Trans>, value: machine.agentVersion ?? t`Not reported` },
        { label: <Trans>SMBIOS UUID</Trans>, value: machine.smbiosUuid, mono: true },
      ]}
    />
  );
}
