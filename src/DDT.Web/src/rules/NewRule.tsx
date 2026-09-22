// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { ApiError } from "@/lib/api";
import { canRun, type SequenceSummary } from "@/sequences/sequences";

import { RuleFields, type RuleLists } from "./RuleFields";
import {
  createRule,
  emptyEdit,
  refusalMessages,
  requestOf,
  rulesQuery,
  type AssignmentRuleKind,
} from "./rules";

import styles from "./NewRule.module.scss";

export interface NewRuleProps {
  kind: AssignmentRuleKind;
  sequences: SequenceSummary[];
  lists: RuleLists;
  onDone: () => void;
}

// A rule is saved only once it is complete, with Add: half a rule cannot be valid.
export function NewRule({ kind, sequences, lists, onDone }: NewRuleProps) {
  const queryClient = useQueryClient();
  const [edit, setEdit] = useState(() => emptyEdit(sequences.find(canRun)?.id ?? ""));

  const create = useMutation({
    mutationFn: () => createRule(requestOf(kind, edit)),
    onSuccess: (rule) => {
      queryClient.setQueryData(rulesQuery.queryKey, (list) =>
        list === undefined ? list : [...list, rule],
      );
      void queryClient.invalidateQueries({ queryKey: rulesQuery.queryKey });
      onDone();
    },
  });

  const refusal = create.error instanceof ApiError ? create.error : null;
  const title = kind === "Mac" ? "New rule by MAC address" : "New rule by model";

  return (
    <section className={styles.form} aria-label={title}>
      <h2 className={styles.title}>{title}</h2>
      <RuleFields
        kind={kind}
        edit={edit}
        messages={(field) => refusalMessages(kind, refusal, field)}
        sequences={sequences}
        lists={lists}
        onChange={(patch) => {
          setEdit((current) => ({ ...current, ...patch }));
          create.reset();
        }}
      />
      {!sequences.some(canRun) && (
        <p className={styles.hint}>
          No sequence can run yet. Create one on the Sequences page and fix its problems first.
        </p>
      )}
      {create.isError && refusal === null && (
        <p className={styles.error} role="alert">
          {`The rule was not added. ${create.error.message}`}
        </p>
      )}
      <div className={styles.actions}>
        <button
          type="button"
          className={styles.button}
          disabled={create.isPending || edit.sequenceId === ""}
          onClick={() => {
            create.mutate();
          }}
        >
          Add
        </button>
        <button
          type="button"
          className={styles.button}
          disabled={create.isPending}
          onClick={onDone}
        >
          Cancel
        </button>
      </div>
    </section>
  );
}
