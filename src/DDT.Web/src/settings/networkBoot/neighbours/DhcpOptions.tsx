// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useState } from "react";

import { Button } from "@/ui/Button";
import { CopyButton } from "@/ui/CopyButton";
import { Facts } from "@/ui/Facts";

import { DhcpHere } from "./DhcpHere";
import { DhcpScopesDialog } from "./DhcpScopesDialog";
import { dhcpCommand, type NetbootNeighbours } from "./neighbours";

// How a DHCP server sends machines to DDT where ProxyDHCP cannot. Microsoft's DHCP server on this computer does it
// with option 60, and any DHCP server with options 66 and 67, which DDT sets itself on the one here.
export function DhcpOptions({ neighbours }: { neighbours: NetbootNeighbours }) {
  const [choosing, setChoosing] = useState(false);
  const here = neighbours.leavesDhcpPort;

  return (
    <>
      {here ? <DhcpHere neighbours={neighbours} /> : null}
      <p className="type-small text-ink-2">
        {here ? (
          <Trans>
            Options 66 and 67 do it too, with ProxyDHCP switched off above. They name one boot file
            for every kind of machine.
          </Trans>
        ) : (
          <Trans>
            Where ProxyDHCP does not reach the machines, such as across a router, the DHCP server
            sends them to DDT with these two options.
          </Trans>
        )}
      </p>
      <Facts
        items={[
          {
            label: <Trans>Option 66, boot server</Trans>,
            value: neighbours.bootServer,
            mono: true,
          },
          { label: <Trans>Option 67, boot file</Trans>, value: neighbours.bootFile, mono: true },
        ]}
      />
      <div className="flex flex-wrap items-center gap-2">
        {here && neighbours.helper ? (
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              setChoosing(true);
            }}
          >
            <Trans>Set them on this DHCP server</Trans>
          </Button>
        ) : null}
        <CopyButton size="sm" variant="quiet" text={dhcpCommand(neighbours)}>
          <Trans>Copy the PowerShell line for a Microsoft DHCP server</Trans>
        </CopyButton>
      </div>

      <DhcpScopesDialog
        isOpen={choosing}
        onClose={() => {
          setChoosing(false);
        }}
      />
    </>
  );
}
