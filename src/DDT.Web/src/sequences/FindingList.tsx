// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { Findings } from "./problems";

import styles from "./FindingList.module.scss";

// Problems keep the sequence from running; warnings only tell. Focusable, so the editor can move to the
// first one.
export function FindingList({ findings }: { findings: Findings }) {
  if (findings.problems.length === 0 && findings.warnings.length === 0) {
    return null;
  }

  return (
    <div className={styles.findings} data-finding tabIndex={-1}>
      {findings.problems.length > 0 && (
        <ul className={styles.problems} aria-label="Problems">
          {findings.problems.map((problem, index) => (
            <li key={index}>{problem.message}</li>
          ))}
        </ul>
      )}
      {findings.warnings.length > 0 && (
        <ul className={styles.warnings} aria-label="Warnings">
          {findings.warnings.map((warning, index) => (
            <li key={index}>{warning.message}</li>
          ))}
        </ul>
      )}
    </div>
  );
}
