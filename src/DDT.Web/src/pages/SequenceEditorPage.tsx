// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";

import { currentUserQuery } from "@/auth/auth";
import { ApiError } from "@/lib/api";
import { SequenceEditor } from "@/sequences/SequenceEditor";
import { sequenceQuery } from "@/sequences/sequences";

import styles from "./SequenceEditorPage.module.scss";

// One sequence, edited in place by administrators and read by everyone else.
export function SequenceEditorPage() {
  // Not strict, so the page reads its parameters in any router that has its path, as its tests do.
  const sequenceId = useParams({ strict: false }).sequenceId ?? "";
  const sequence = useQuery(sequenceQuery(sequenceId));
  const user = useQuery(currentUserQuery).data ?? null;

  const isAdministrator = user?.roles.includes("Administrator") === true;
  // The editor keeps its copy once open, and says itself when the sequence goes away.
  const missing =
    sequence.data === undefined &&
    sequence.error instanceof ApiError &&
    sequence.error.status === 404;

  return (
    <div className={styles.page}>
      <Link to="/sequences" className={styles.back}>
        All sequences
      </Link>

      {missing && (
        <section className={styles.notice}>
          <h1>Sequence not found</h1>
          <p>This sequence does not exist. It may have been deleted.</p>
        </section>
      )}

      {!missing && sequence.isError && sequence.data === undefined && (
        <p className={styles.error}>The sequence could not be loaded.</p>
      )}

      {sequence.data !== undefined && user !== null && (
        <SequenceEditor key={sequenceId} initial={sequence.data} readOnly={!isAdministrator} />
      )}
    </div>
  );
}
