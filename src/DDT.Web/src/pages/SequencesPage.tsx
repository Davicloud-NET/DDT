// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { isActive } from "@/deployments/deployments";
import { plural } from "@/lib/format";
import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { machinesQuery } from "@/machines/machines";
import { describeRule, rulesQuery } from "@/rules/rules";
import { NewSequence } from "@/sequences/NewSequence";
import { deletionConsequence, rulesChoosing } from "@/sequences/sequenceList";
import { deleteSequence, sequencesQuery, type SequenceSummary } from "@/sequences/sequences";

import styles from "./SequencesPage.module.scss";

// What running the sequence does, from the facts the list carries.
function facts(sequence: SequenceSummary): string[] {
  return [
    sequence.erasesDisk ? "Erases the disk" : "Keeps the disk",
    ...(sequence.continuesInWindows ? ["Continues in Windows"] : []),
    ...(sequence.needsComputerName ? ["Joins the domain"] : []),
  ];
}

export function SequencesPage() {
  const queryClient = useQueryClient();
  const sequences = useQuery(sequencesQuery);
  const rules = useQuery(rulesQuery);
  const machines = useQuery(machinesQuery);
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(30_000);

  const [deleteTarget, setDeleteTarget] = useState<SequenceSummary | null>(null);

  const isAdministrator = user?.roles.includes("Administrator") === true;

  const remove = useMutation({
    mutationFn: (id: string) => deleteSequence(id),
    onSuccess: () => {
      setDeleteTarget(null);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: sequencesQuery.queryKey });
    },
  });

  const list = sequences.data ?? [];
  const ruleList = rules.data ?? [];
  const activeRuns = (sequenceId: string) =>
    (machines.data ?? []).filter(
      (machine) => isActive(machine.deployment) && machine.deployment?.sequenceId === sequenceId,
    ).length;

  return (
    <div className={styles.page}>
      <h1>Sequences</h1>
      <p className={styles.intro}>
        A sequence is the list of steps a machine runs, such as partitioning the disk, applying an
        image and running scripts. A sequence with problems is kept as a draft and cannot run until
        they are fixed.
      </p>

      {isAdministrator && <NewSequence taken={list.map((sequence) => sequence.name)} />}

      {sequences.isError && <p className={styles.error}>The sequence list could not be loaded.</p>}

      {sequences.isSuccess && list.length === 0 && (
        <section className={styles.empty}>
          <h2 className={styles.emptyTitle}>No sequences yet</h2>
          <p>
            {isAdministrator
              ? "Start from the Install Windows template: it partitions the disk, applies an image, adds the drivers for the machine's model and writes the answer file. Or start empty and add the steps yourself."
              : "An administrator creates sequences here. Until then, no machine can be given one."}
          </p>
        </section>
      )}

      {list.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Steps</th>
              <th scope="col">Problems</th>
              <th scope="col">Chosen by rules</th>
              <th scope="col">Updated</th>
              {isAdministrator && <th scope="col">Actions</th>}
            </tr>
          </thead>
          <tbody>
            {list.map((sequence) => {
              const choosing = rulesChoosing(ruleList, sequence.id);

              return (
                <tr key={sequence.id}>
                  <td>
                    <Link
                      to="/sequences/$sequenceId"
                      params={{ sequenceId: sequence.id }}
                      className={styles.link}
                    >
                      {sequence.name}
                    </Link>
                    {sequence.description !== null && (
                      <div className={styles.secondary}>{sequence.description}</div>
                    )}
                  </td>
                  <td>
                    <div>{plural(sequence.stepCount, "step")}</div>
                    <div className={styles.secondary}>{facts(sequence).join(", ")}</div>
                  </td>
                  <td>
                    {sequence.problemCount > 0 && (
                      <div className={styles.problem}>
                        {`${plural(sequence.problemCount, "problem")}, cannot run`}
                      </div>
                    )}
                    {sequence.warningCount > 0 && (
                      <div className={styles.warning}>
                        {plural(sequence.warningCount, "warning")}
                      </div>
                    )}
                    {sequence.problemCount === 0 && sequence.warningCount === 0 && (
                      <div>Ready to run</div>
                    )}
                  </td>
                  <td>
                    {choosing.length === 0 ? (
                      <span className={styles.secondary}>No rule</span>
                    ) : (
                      <ul className={styles.rules}>
                        {choosing.map((rule) => (
                          <li key={rule.id}>{describeRule(rule)}</li>
                        ))}
                      </ul>
                    )}
                  </td>
                  <td title={new Date(sequence.updatedUtc).toLocaleString()}>
                    <div>{relativeTime(sequence.updatedUtc, now)}</div>
                    {sequence.updatedBy !== null && (
                      <div className={styles.secondary}>by {sequence.updatedBy}</div>
                    )}
                  </td>
                  {isAdministrator && (
                    <td>
                      <button
                        type="button"
                        className={styles.delete}
                        aria-label={`Delete ${sequence.name}`}
                        onClick={() => {
                          remove.reset();
                          setDeleteTarget(sequence);
                        }}
                      >
                        Delete
                      </button>
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      {deleteTarget !== null && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              setDeleteTarget(null);
            }
          }}
          title={`Delete ${deleteTarget.name}?`}
          consequence={deletionConsequence(
            deleteTarget,
            rulesChoosing(ruleList, deleteTarget.id),
            activeRuns(deleteTarget.id),
          )}
          confirmLabel="Delete sequence"
          busy={remove.isPending}
          error={remove.isError ? remove.error.message : null}
          onConfirm={() => {
            remove.mutate(deleteTarget.id);
          }}
        />
      )}
    </div>
  );
}
