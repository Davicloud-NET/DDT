// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import { ConfirmDialog } from "@/components/ConfirmDialog";

import type { SequenceView } from "./sequences";

import styles from "./SequenceConflict.module.scss";

export interface SequenceConflictProps {
  // Their copy, once it is read.
  theirs: SequenceView | null;
  // What they changed against the copy this page started from, such as "the name" or a step's name.
  changes: string[];
  onTakeTheirs: () => void;
  onKeepMine: () => void;
}

// Another administrator saved while this page had unsaved edits. Nothing is saved until one copy is chosen.
export function SequenceConflict({
  theirs,
  changes,
  onTakeTheirs,
  onKeepMine,
}: SequenceConflictProps) {
  const [confirming, setConfirming] = useState(false);

  const who = theirs?.updatedBy ?? "Another administrator";
  const when = theirs === null ? "" : ` at ${new Date(theirs.updatedUtc).toLocaleTimeString()}`;
  const theirChanges =
    changes.length === 0 ? "" : ` Their changes to ${changes.join(", ")} are lost.`;

  return (
    <section className={styles.banner} role="alert">
      <p className={styles.text}>
        {`${who} saved this sequence${when} while you were editing. Your changes since your last save are not saved.`}
      </p>
      {changes.length > 0 && <p className={styles.text}>{`They changed ${changes.join(", ")}.`}</p>}
      {theirs === null && <p className={styles.text}>Reading their version.</p>}
      <div className={styles.actions}>
        <button
          type="button"
          className={styles.button}
          disabled={theirs === null}
          onClick={onTakeTheirs}
        >
          Use theirs
        </button>
        <button
          type="button"
          className={styles.button}
          disabled={theirs === null}
          onClick={() => {
            setConfirming(true);
          }}
        >
          Keep mine
        </button>
      </div>
      <p className={styles.hint}>
        Use theirs throws your unsaved changes away. Keep mine saves your version over theirs.
      </p>

      {confirming && (
        <ConfirmDialog
          open
          onOpenChange={setConfirming}
          title="Save your version over theirs?"
          consequence={`Your version replaces the one ${who} saved${when}.${theirChanges}`}
          confirmLabel="Save my version"
          busy={false}
          error={null}
          onConfirm={() => {
            setConfirming(false);
            onKeepMine();
          }}
        />
      )}
    </section>
  );
}
