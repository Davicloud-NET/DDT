// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { SettingsGroup } from "../../parts/SettingsGroup";

import { DhcpOptions } from "./DhcpOptions";
import { neighboursQuery } from "./neighbours";
import { PortOwners } from "./PortOwners";
import { WdsChoices } from "./WdsChoices";

// What else answers netboot on the server's computer. An MDT shop's server runs WDS and often Microsoft's DHCP
// server, which hold the ports DDT would answer on, so the page says who holds them and offers what fits.
export function NeighboursGroup() {
  const neighbours = useQuery(neighboursQuery).data;

  // A server that does not say, such as an older one, shows nothing here
  if (neighbours === undefined) {
    return null;
  }

  return (
    <SettingsGroup title={<Trans>Next to DHCP and WDS</Trans>}>
      {neighbours.ports === null ? null : <PortOwners ports={neighbours.ports} />}
      <DhcpOptions neighbours={neighbours} />
      {neighbours.wds.installed ? <WdsChoices neighbours={neighbours} /> : null}
    </SettingsGroup>
  );
}
