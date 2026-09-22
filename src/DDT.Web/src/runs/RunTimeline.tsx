// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { TimelineEntry } from "@/runs/runs";

import styles from "./RunTimeline.module.scss";

export interface RunTimelineProps {
  entries: readonly TimelineEntry[];
}

export function RunTimeline({ entries }: RunTimelineProps) {
  return (
    <section className={styles.timeline} aria-label="Timeline">
      <h3>Timeline</h3>
      <ol className={styles.entries}>
        {entries.map((entry) => (
          <li key={entry.key} className={styles.entry}>
            <time className={styles.time} dateTime={entry.utc}>
              {new Date(entry.utc).toLocaleString()}
            </time>
            <span>{entry.text}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}
