// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";

import { currentUserQuery } from "@/auth/auth";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { DeploymentCell } from "@/machines/DeploymentCell";
import { MachineActions } from "@/machines/MachineActions";
import { formatMac, isStray, machinesQuery } from "@/machines/machines";
import { useMachineActions } from "@/machines/useMachineActions";

import styles from "./MachinesPage.module.scss";

export function MachinesPage() {
  const machines = useQuery(machinesQuery);
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(5_000);
  const actions = useMachineActions();

  const roles = user?.roles ?? [];
  const canDecide = roles.includes("Administrator") || roles.includes("Operator");

  const list = machines.data ?? [];

  // Offered once per address, on its first row, when more than one stray came from it.
  const straysByAddress = new Map<string, number>();
  for (const machine of list) {
    if (isStray(machine) && machine.firstSeenAddress !== null) {
      straysByAddress.set(
        machine.firstSeenAddress,
        (straysByAddress.get(machine.firstSeenAddress) ?? 0) + 1,
      );
    }
  }
  const bulkOffered = new Set<string>();

  return (
    <div className={styles.page}>
      <h1>Machines</h1>

      {machines.isError && <p className={styles.error}>The machine list could not be loaded.</p>}

      {machines.isSuccess && list.length === 0 && (
        <section className={styles.empty}>
          <h2 className={styles.emptyTitle}>No machines yet</h2>
          <p>
            Machines appear here once the DDT agent checks in. Nothing has registered with this
            server.
          </p>
        </section>
      )}

      {list.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">State</th>
              <th scope="col">Machine</th>
              <th scope="col">MAC</th>
              <th scope="col">SMBIOS UUID</th>
              <th scope="col">Last seen</th>
              <th scope="col">Deployment</th>
              {canDecide && <th scope="col">Actions</th>}
            </tr>
          </thead>
          <tbody>
            {list.map((machine) => {
              const address = machine.firstSeenAddress;
              const strays =
                isStray(machine) && address !== null ? (straysByAddress.get(address) ?? 0) : 0;
              const offerBulk = strays > 1 && address !== null && !bulkOffered.has(address);

              if (offerBulk) {
                bulkOffered.add(address);
              }

              return (
                <tr key={machine.id}>
                  <td>
                    <span className={styles.state} data-state={machine.state}>
                      {machine.state}
                    </span>
                    {machine.signedInBy !== null && (
                      <div className={styles.secondary}>Signed in by {machine.signedInBy}</div>
                    )}
                  </td>
                  <td>
                    <div>{machine.assignedName ?? machine.model ?? "Unknown model"}</div>
                    <div className={styles.secondary}>
                      {[machine.manufacturer, machine.serialNumber].filter(Boolean).join(", ")}
                    </div>
                  </td>
                  <td className={styles.mono}>{formatMac(machine.primaryMac)}</td>
                  <td className={styles.mono}>{machine.smbiosUuid}</td>
                  <td title={new Date(machine.lastSeenUtc).toLocaleString()}>
                    <div>{relativeTime(machine.lastSeenUtc, now)}</div>
                    <div className={styles.secondary}>{machine.lastSeenAddress}</div>
                  </td>
                  <td>
                    <DeploymentCell deployment={machine.deployment} now={now} />
                  </td>
                  {canDecide && (
                    <td>
                      <MachineActions
                        machine={machine}
                        actions={actions}
                        strays={offerBulk ? { address, count: strays } : null}
                      />
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      {actions.decide.isError && <p className={styles.error}>{actions.decide.error.message}</p>}
      {actions.remove.isError && <p className={styles.error}>{actions.remove.error.message}</p>}
      {actions.cancel.isError && (
        <p className={styles.error} role="alert">
          {actions.cancel.error.message}
        </p>
      )}
    </div>
  );
}
