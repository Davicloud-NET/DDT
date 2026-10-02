// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useState } from "react";

import { Button } from "@/ui/Button";
import { CopyButton } from "@/ui/CopyButton";
import { Facts } from "@/ui/Facts";
import { Notice } from "@/ui/Notice";

import { DhcpScopesDialog } from "./DhcpScopesDialog";
import { dhcpCommand, type NetbootNeighbours } from "./neighbours";

// Options 66 and 67 send machines to DDT from a DHCP server, in place of ProxyDHCP. For the Microsoft DHCP server on
// this computer DDT sets them itself; for any other it gives the two values.
export function DhcpOptions({ neighbours }: { neighbours: NetbootNeighbours }) {
  const [choosing, setChoosing] = useState(false);
  const here = neighbours.dhcp.running;

  return (
    <>
      {here ? (
        <Notice
          tone="attention"
          actions={
            neighbours.helper ? (
              <Button
                size="sm"
                onPress={() => {
                  setChoosing(true);
                }}
              >
                <Trans>Set them on this DHCP server</Trans>
              </Button>
            ) : null
          }
        >
          <Trans>
            Microsoft's DHCP server runs on this computer and answers on UDP 67, so ProxyDHCP
            cannot. Its options 66 and 67 send machines to DDT instead, with ProxyDHCP switched off
            above.
          </Trans>
        </Notice>
      ) : (
        <p className="type-small text-ink-2">
          <Trans>
            Where ProxyDHCP does not reach the machines, such as across a router, the DHCP server
            sends them to DDT with these two options.
          </Trans>
        </p>
      )}
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
      <div>
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
