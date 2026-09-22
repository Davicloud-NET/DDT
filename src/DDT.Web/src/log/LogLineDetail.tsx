// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { clockNote, type MachineLogEntry } from "./log";

import styles from "./LogLineDetail.module.scss";

export interface LogLineDetailProps {
  line: MachineLogEntry;
  // The step the agent ran when it sent the line, as "step 4, Apply image".
  step: string | null;
  onClose: () => void;
}

// A row shows one line of a message; this shows all of it with every time the server knows.
export function LogLineDetail({ line, step, onClose }: LogLineDetailProps) {
  const note = clockNote(line);

  return (
    <section className={styles.detail} aria-label="Log line">
      <p className={styles.facts}>
        {line.level} at {new Date(line.timestampUtc).toLocaleString()}
        {step !== null && `, during ${step}`}. Received{" "}
        {new Date(line.receivedUtc).toLocaleString()}.{note !== null && ` ${note}`}
      </p>
      <pre className={styles.message}>{line.message}</pre>
      <div>
        <button type="button" className={styles.close} onClick={onClose}>
          Close
        </button>
      </div>
    </section>
  );
}
