// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { relativeTime } from "@/lib/relativeTime";
import { MachineActions } from "@/machines/MachineActions";
import { secureBootFact } from "@/machines/secureBoot";
import { formatMac, type MachineSummary } from "@/machines/machines";
import type { MachineActionState } from "@/machines/useMachineActions";

import styles from "./MachineHeader.module.scss";

export interface MachineHeaderProps {
  machine: MachineSummary;
  // Null for someone who may only look.
  actions: MachineActionState | null;
  // Why the machine would get the sequence it gets, as the server explains it.
  resolution: string | null;
  now: number;
}

export function MachineHeader({ machine, actions, resolution, now }: MachineHeaderProps) {
  const model = [machine.manufacturer, machine.model].filter((part) => part !== null).join(" ");

  return (
    <header className={styles.header}>
      <div className={styles.title}>
        <h1>{machine.assignedName ?? machine.model ?? "Unknown model"}</h1>
        <span className={styles.state} data-state={machine.state}>
          {machine.state}
        </span>
        {machine.signedInBy !== null && (
          <span className={styles.secondary}>Signed in by {machine.signedInBy}</span>
        )}
      </div>

      <dl className={styles.facts}>
        <div>
          <dt>Model</dt>
          <dd>{model === "" ? "Not reported" : model}</dd>
        </div>
        <div>
          <dt>Serial number</dt>
          <dd>{machine.serialNumber ?? "Not reported"}</dd>
        </div>
        <div>
          <dt>MAC addresses</dt>
          <dd className={styles.mono}>{machine.macAddresses.map(formatMac).join(", ")}</dd>
        </div>
        <div>
          <dt>SMBIOS UUID</dt>
          <dd className={styles.mono}>{machine.smbiosUuid}</dd>
        </div>
        <div>
          <dt>Last seen</dt>
          <dd title={new Date(machine.lastSeenUtc).toLocaleString()}>
            {relativeTime(machine.lastSeenUtc, now)}
            {machine.lastSeenAddress !== null && ` from ${machine.lastSeenAddress}`}
          </dd>
        </div>
        <div>
          <dt>Agent</dt>
          <dd>{machine.agentVersion ?? "Not reported"}</dd>
        </div>
        <div>
          <dt>Secure Boot</dt>
          <dd>{secureBootFact(machine)}</dd>
        </div>
        <div>
          <dt>Disks</dt>
          <dd>
            {machine.disks ??
              (machine.eligibleDiskCount === 0 ? "No disk DDT can install on" : "Not reported")}
          </dd>
        </div>
      </dl>

      {resolution !== null && <p className={styles.secondary}>{resolution}</p>}

      {actions !== null && <MachineActions machine={machine} actions={actions} />}
    </header>
  );
}
