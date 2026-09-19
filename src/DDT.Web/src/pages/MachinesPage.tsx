import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { currentUserQuery } from "@/auth/auth";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import {
  approveMachine,
  formatMac,
  isStray,
  machinesQuery,
  rejectMachine,
  removeMachine,
  removeWaitingFrom,
  upsertMachine,
} from "@/machines/machines";

import styles from "./MachinesPage.module.scss";

export function MachinesPage() {
  const queryClient = useQueryClient();
  const machines = useQuery(machinesQuery);
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(5_000);

  const roles = user?.roles ?? [];
  const canDecide = roles.includes("Administrator") || roles.includes("Operator");

  const decide = useMutation({
    mutationFn: ({ id, approve }: { id: string; approve: boolean }) =>
      approve ? approveMachine(id) : rejectMachine(id),
    onSuccess: (machine) => {
      upsertMachine(queryClient, machine);
    },
    // Usually someone else decided first, or the machine registered again. Show what is stored now.
    onError: () => {
      void queryClient.invalidateQueries({ queryKey: machinesQuery.queryKey });
    },
  });

  const remove = useMutation({
    mutationFn: (target: { id: string } | { address: string }) =>
      "id" in target ? removeMachine(target.id) : removeWaitingFrom(target.address),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: machinesQuery.queryKey });
    },
  });

  const list = machines.data ?? [];
  const busy = decide.isPending || remove.isPending;

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
              {canDecide && <th scope="col">Decision</th>}
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
                  {canDecide && (
                    <td>
                      {(machine.state === "Pending" || machine.state === "Approved") && (
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
                          {isStray(machine) && (
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
                      )}
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
    </div>
  );
}
