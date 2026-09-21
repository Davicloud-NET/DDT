// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { endDeployment, isActive } from "@/deployments/deployments";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { AssignDialog } from "@/machines/AssignDialog";
import { DeploymentCell } from "@/machines/DeploymentCell";
import {
  approveMachine,
  formatMac,
  isRemovable,
  isStray,
  machineLabel,
  machinesQuery,
  rejectMachine,
  removeMachine,
  removeWaitingFrom,
  upsertMachine,
  type MachineState,
} from "@/machines/machines";

import styles from "./MachinesPage.module.scss";

// The server refuses an assignment while the machine deploys, and for good in these states.
const assignableStates: readonly MachineState[] = ["Pending", "Approved", "Done", "Failed"];

// Reject is terminal. It is offered where a machine may be one to throw out; a running deployment is
// stopped with Stop instead.
const rejectableStates: readonly MachineState[] = ["Pending", "Approved", "Failed"];

export function MachinesPage() {
  const queryClient = useQueryClient();
  const machines = useQuery(machinesQuery);
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(5_000);

  const [assignTo, setAssignTo] = useState<string | null>(null);
  const [stopOn, setStopOn] = useState<{ machineId: string; deploymentId: string } | null>(null);

  const roles = user?.roles ?? [];
  const canDecide = roles.includes("Administrator") || roles.includes("Operator");

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: machinesQuery.queryKey });
  };

  const decide = useMutation({
    mutationFn: ({ id, approve }: { id: string; approve: boolean }) =>
      approve ? approveMachine(id) : rejectMachine(id),
    onSuccess: (machine) => {
      upsertMachine(queryClient, machine);
    },
    // Usually someone else decided first, or the machine registered again. Show what is stored now.
    onError: refresh,
  });

  const remove = useMutation({
    mutationFn: (target: { id: string } | { address: string }) =>
      "id" in target ? removeMachine(target.id) : removeWaitingFrom(target.address),
    onSettled: refresh,
  });

  const cancel = useMutation({
    mutationFn: (id: string) => endDeployment(id),
    onSuccess: (machine) => {
      upsertMachine(queryClient, machine);
    },
    onError: refresh,
  });

  const stop = useMutation({
    mutationFn: (id: string) => endDeployment(id),
    onSuccess: (machine) => {
      upsertMachine(queryClient, machine);
      setStopOn(null);
    },
    onError: refresh,
  });

  const list = machines.data ?? [];
  const busy = decide.isPending || remove.isPending || cancel.isPending || stop.isPending;
  const assignTarget = list.find((machine) => machine.id === assignTo) ?? null;
  // The confirmation is for the deployment that was running when Stop was clicked. Once that one ended,
  // the dialog closes, so a late confirm cannot stop another deployment on the same machine.
  const stopTarget =
    stopOn === null
      ? null
      : (list.find(
          (machine) =>
            machine.id === stopOn.machineId &&
            machine.deployment?.id === stopOn.deploymentId &&
            machine.deployment.state === "Running",
        ) ?? null);

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
              const deploymentState = machine.deployment?.state;
              const running = deploymentState === "Running" ? machine.deployment : null;

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
                      <div className={styles.actions}>
                        {machine.state === "Pending" && (
                          <button
                            type="button"
                            className={styles.approve}
                            disabled={busy}
                            onClick={() => {
                              decide.mutate({ id: machine.id, approve: true });
                            }}
                          >
                            Approve
                          </button>
                        )}
                        {assignableStates.includes(machine.state) &&
                          !isActive(machine.deployment) && (
                            <button
                              type="button"
                              className={styles.approve}
                              disabled={busy}
                              onClick={() => {
                                setAssignTo(machine.id);
                              }}
                            >
                              Assign
                            </button>
                          )}
                        {deploymentState === "Assigned" && (
                          <button
                            type="button"
                            className={styles.reject}
                            disabled={busy}
                            onClick={() => {
                              cancel.mutate(machine.id);
                            }}
                          >
                            Cancel
                          </button>
                        )}
                        {running !== null && (
                          <button
                            type="button"
                            className={styles.reject}
                            disabled={busy}
                            onClick={() => {
                              stop.reset();
                              setStopOn({ machineId: machine.id, deploymentId: running.id });
                            }}
                          >
                            Stop
                          </button>
                        )}
                        {rejectableStates.includes(machine.state) && (
                          <button
                            type="button"
                            className={styles.reject}
                            disabled={busy}
                            onClick={() => {
                              decide.mutate({ id: machine.id, approve: false });
                            }}
                          >
                            Reject
                          </button>
                        )}
                        {isRemovable(machine) && (
                          <button
                            type="button"
                            className={styles.reject}
                            disabled={busy}
                            onClick={() => {
                              remove.mutate({ id: machine.id });
                            }}
                          >
                            Remove
                          </button>
                        )}
                        {offerBulk && (
                          <button
                            type="button"
                            className={styles.reject}
                            disabled={busy}
                            onClick={() => {
                              remove.mutate({ address });
                            }}
                          >
                            Remove all {strays} from {address}
                          </button>
                        )}
                      </div>
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      {decide.isError && <p className={styles.error}>{decide.error.message}</p>}
      {remove.isError && <p className={styles.error}>{remove.error.message}</p>}
      {cancel.isError && (
        <p className={styles.error} role="alert">
          {cancel.error.message}
        </p>
      )}

      {assignTarget !== null && (
        <AssignDialog
          machine={assignTarget}
          onClose={() => {
            setAssignTo(null);
          }}
        />
      )}

      {stopTarget !== null && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              setStopOn(null);
            }
          }}
          title="Stop the deployment?"
          consequence={`This stops the deployment on ${machineLabel(stopTarget)}. Its disk is left half written; assign an image again to deploy it.`}
          confirmLabel="Stop deployment"
          busy={stop.isPending}
          error={stop.isError ? stop.error.message : null}
          onConfirm={() => {
            stop.mutate(stopTarget.id);
          }}
        />
      )}
    </div>
  );
}
