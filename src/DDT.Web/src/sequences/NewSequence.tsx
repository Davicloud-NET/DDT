// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { useId } from "react";

import { uniqueName, withNewStepIds } from "./sequenceList";
import {
  createSequence,
  SEQUENCE_VERSION,
  sequenceQuery,
  sequencesQuery,
  templatesQuery,
  type CreateSequenceRequest,
} from "./sequences";

import styles from "./NewSequence.module.scss";

export interface NewSequenceProps {
  // The names in use, so a new sequence gets one of its own.
  taken: readonly string[];
}

// Creates a sequence at once and opens it in the editor, where it is named and changed in place.
export function NewSequence({ taken }: NewSequenceProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const templates = useQuery(templatesQuery);
  const titleId = useId();

  const create = useMutation({
    mutationFn: (request: CreateSequenceRequest) => createSequence(request),
    onSuccess: (view) => {
      queryClient.setQueryData(sequenceQuery(view.id).queryKey, view);
      void queryClient.invalidateQueries({ queryKey: sequencesQuery.queryKey });
      void navigate({ to: "/sequences/$sequenceId", params: { sequenceId: view.id } });
    },
  });

  return (
    <section className={styles.panel} aria-labelledby={titleId}>
      <h2 id={titleId} className={styles.title}>
        New sequence
      </h2>

      <ul className={styles.choices}>
        {(templates.data ?? []).map((template) => (
          <li key={template.key} className={styles.choice}>
            <button
              type="button"
              className={styles.button}
              disabled={create.isPending}
              onClick={() => {
                create.mutate({
                  name: uniqueName(template.name, taken),
                  description: template.description,
                  definition: withNewStepIds(template.definition),
                });
              }}
            >
              {`New from the ${template.name} template`}
            </button>
            <span className={styles.hint}>{template.description}</span>
          </li>
        ))}
        <li className={styles.choice}>
          <button
            type="button"
            className={styles.button}
            disabled={create.isPending}
            onClick={() => {
              create.mutate({
                name: uniqueName("New sequence", taken),
                description: null,
                definition: { version: SEQUENCE_VERSION, steps: [] },
              });
            }}
          >
            New empty sequence
          </button>
          <span className={styles.hint}>Starts without steps; you add them in the editor.</span>
        </li>
      </ul>

      {templates.isError && <p className={styles.error}>The templates could not be loaded.</p>}
      {create.isPending && (
        <p className={styles.hint} role="status">
          Creating the sequence
        </p>
      )}
      {create.isError && (
        <p className={styles.error} role="alert">
          {`The sequence was not created. ${create.error.message}`}
        </p>
      )}
    </section>
  );
}
