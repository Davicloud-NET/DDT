import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { currentUserQuery } from "@/auth/auth";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import {
  approveMachine,
  formatMac,
  machinesQuery,
  rejectMachine,
  upsertMachine,
} from "@/machines/machines";

import { EnrollmentTokensPanel } from "./EnrollmentTokensPanel";
import styles from "./MachinesPage.module.scss";

export function MachinesPage() {
  const queryClient = useQueryClient();
  const machines = useQuery(machinesQuery);
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(5_000);

  const roles = user?.roles ?? [];
  const canDecide = roles.includes("Administrator") || roles.includes("Operator");
  const isAdministrator = roles.includes("Administrator");

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

  const list = machines.data ?? [];

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
            {list.map((machine) => (
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
                            disabled={decide.isPending}
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
                          disabled={decide.isPending}
                          onClick={() => {
                            decide.mutate({ id: machine.id, approve: false });
                          }}
                        >
                          Reject
                        </button>
                      </div>
                    )}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {decide.isError && <p className={styles.error}>{decide.error.message}</p>}

      {isAdministrator && <EnrollmentTokensPanel />}
    </div>
  );
}
