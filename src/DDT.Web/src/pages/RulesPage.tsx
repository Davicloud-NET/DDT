// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { useNow } from "@/lib/useNow";
import { formatMac, machinesQuery, modelsQuery } from "@/machines/machines";
import { NewRule } from "@/rules/NewRule";
import { RuleRow } from "@/rules/RuleRow";
import {
  deleteRule,
  describeRule,
  ruleDeletionConsequence,
  ruleMatches,
  rulesQuery,
  type AssignmentRuleKind,
  type AssignmentRuleView,
} from "@/rules/rules";
import { sequencesQuery } from "@/sequences/sequences";

import styles from "./RulesPage.module.scss";

export function RulesPage() {
  const queryClient = useQueryClient();
  const rules = useQuery(rulesQuery);
  const sequences = useQuery(sequencesQuery);
  const machines = useQuery(machinesQuery);
  const models = useQuery(modelsQuery);
  const user = useQuery(currentUserQuery).data ?? null;
  const now = useNow(30_000);
  const lists = { macs: useId(), models: useId(), manufacturers: useId() };

  const [adding, setAdding] = useState<AssignmentRuleKind | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<AssignmentRuleView | null>(null);

  const isAdministrator = user?.roles.includes("Administrator") === true;

  const remove = useMutation({
    mutationFn: (id: string) => deleteRule(id),
    onSuccess: () => {
      setDeleteTarget(null);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: rulesQuery.queryKey });
    },
  });

  const list = rules.data ?? [];
  const sequenceList = sequences.data ?? [];
  const machineList = machines.data ?? [];
  const modelList = models.data ?? [];
  const macs = [...new Set(machineList.flatMap((machine) => machine.macAddresses))];
  const manufacturers = [
    ...new Set(
      modelList.flatMap((model) => (model.manufacturer === null ? [] : [model.manufacturer])),
    ),
  ];

  const section = (kind: AssignmentRuleKind, title: string) => {
    const ofKind = list.filter((rule) => rule.kind === kind);

    return (
      ofKind.length > 0 && (
        <section className={styles.section}>
          <h2>{title}</h2>
          <ul className={styles.rules}>
            {ofKind.map((rule) => (
              <RuleRow
                key={rule.id}
                rule={rule}
                sequences={sequenceList}
                matches={ruleMatches(rule, machineList, modelList)}
                canEdit={isAdministrator}
                lists={lists}
                now={now}
                onDelete={() => {
                  remove.reset();
                  setDeleteTarget(rule);
                }}
              />
            ))}
          </ul>
        </section>
      )
    );
  };

  return (
    <div className={styles.page}>
      <h1>Rules</h1>
      <p className={styles.statement}>
        A rule chooses the sequence for a machine that has none. It never authorizes a machine: an
        operator still approves it on the Machines page, or someone signs in at it.
      </p>
      <p className={styles.intro}>
        Which choice counts: a sequence assigned on the web or chosen at the machine comes before
        every rule. Then a rule for one of the machine's MAC addresses, then a rule for its model:
        the exact model before a model ending in *, the longest such prefix first, and a rule that
        names the manufacturer before one for any manufacturer.
      </p>

      {isAdministrator && (
        <div className={styles.actions}>
          <button
            type="button"
            className={styles.button}
            onClick={() => {
              setAdding("Mac");
            }}
          >
            Add MAC rule
          </button>
          <button
            type="button"
            className={styles.button}
            onClick={() => {
              setAdding("Model");
            }}
          >
            Add model rule
          </button>
        </div>
      )}

      {isAdministrator && adding !== null && (
        <NewRule
          key={adding}
          kind={adding}
          sequences={sequenceList}
          lists={lists}
          onDone={() => {
            setAdding(null);
          }}
        />
      )}

      {rules.isError && <p className={styles.error}>The rules could not be loaded.</p>}

      {rules.isSuccess && list.length === 0 && (
        <section className={styles.empty}>
          <h2 className={styles.emptyTitle}>No rules</h2>
          <p>
            Without rules an operator assigns a sequence to each machine on the Machines page.
            {isAdministrator
              ? " Add a rule to choose one by MAC address or hardware model."
              : " An administrator adds rules here."}
          </p>
        </section>
      )}

      {section("Mac", "Rules by MAC address")}
      {section("Model", "Rules by model")}

      <datalist id={lists.macs}>
        {macs.map((mac) => (
          <option key={mac} value={formatMac(mac)} />
        ))}
      </datalist>
      <datalist id={lists.models}>
        {modelList.map((model) => (
          <option key={`${model.manufacturer ?? ""}/${model.model}`} value={model.model} />
        ))}
      </datalist>
      <datalist id={lists.manufacturers}>
        {manufacturers.map((manufacturer) => (
          <option key={manufacturer} value={manufacturer} />
        ))}
      </datalist>

      {deleteTarget !== null && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              setDeleteTarget(null);
            }
          }}
          title={`Delete the rule for ${describeRule(deleteTarget)}?`}
          consequence={ruleDeletionConsequence(deleteTarget)}
          confirmLabel="Delete rule"
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
