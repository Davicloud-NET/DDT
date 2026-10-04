// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { Facts } from "@/ui/Facts";

import type { NetbootPort, NetbootPortOwner } from "./neighbours";

const purposes: Record<number, () => string> = {
  67: () => t`UDP 67, DHCP and ProxyDHCP`,
  69: () => t`UDP 69, TFTP`,
  4011: () => t`UDP 4011, PXE boot server`,
};

// Who holds each netboot port on the server's computer. On Windows a second program can bind a port that another
// one answers on, so a listener that started says little.
export function PortOwners({ ports }: { ports: NetbootPort[] }) {
  return (
    <>
      <p className="type-small text-ink-2">
        <Trans>
          The programs that hold the netboot ports on this computer. Where another program holds a
          port with DDT, Windows may hand the machines to it.
        </Trans>
      </p>
      <Facts
        items={ports.map((port) => ({
          label: purposes[port.port]?.() ?? String(port.port),
          value:
            port.owners.length === 0 ? (
              <span className="text-muted">
                <Trans>Nobody</Trans>
              </span>
            ) : (
              port.owners.map(ownerName).join(", ")
            ),
        }))}
      />
    </>
  );
}

function ownerName(owner: NetbootPortOwner): string {
  const process = owner.process === "" ? t`a process that has ended` : owner.process;
  const id = owner.processId;

  switch (owner.service) {
    case "DDT":
      return "DDT";
    case "DHCP":
      return t`Microsoft's DHCP server (${process}, process ${id})`;
    case "WDS":
      return t`Windows Deployment Services (${process}, process ${id})`;
    case null:
      return t`${process} (process ${id})`;
  }
}
