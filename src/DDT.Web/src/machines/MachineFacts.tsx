// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Facts, Panel } from "@/ui/Layout";

import { factRows } from "./facts";
import type { MachineSummary } from "./machines";

// What the machine reported besides its identity, which conditions and rules can test: memory, processor, TPM, its
// network and its firmware's names, each under the name a condition gives it. The header shows its identity.
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
