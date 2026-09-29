// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Facts } from "@/ui/Facts";
import { Panel } from "@/ui/Panel";

import { factRows } from "./facts";
import type { MachineSummary } from "./machines";

// What the machine reported besides its identity: memory, processor, TPM, network and firmware names. Conditions
// and rules can test these, and each one shows under the name a condition uses. The header shows the identity.
export function MachineFacts({ machine }: { machine: MachineSummary }) {
  const rows = factRows(machine);

  return (
    <Panel title={<Trans>Machine facts</Trans>}>
      {rows.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>
            The agent on this machine reported no facts. A newer agent reports them at the machine's
            next netboot.
          </Trans>
        </p>
      ) : (
        <Facts
          items={rows.map((row) => ({ label: row.label, value: row.value, mono: row.mono }))}
        />
      )}
    </Panel>
  );
}
