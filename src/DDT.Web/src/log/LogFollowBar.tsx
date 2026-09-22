// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@/lib/format";

import styles from "./LogFollowBar.module.scss";

export interface LogFollowBarProps {
  // Lines that arrived since following paused.
  newLines: number;
  onFollow: () => void;
}

// Shown while following is paused. It says in summary what arrived, because the log itself is not read out.
export function LogFollowBar({ newLines, onFollow }: LogFollowBarProps) {
  return (
    <div className={styles.bar} role="status">
      <span>Paused.{newLines > 0 && ` ${plural(newLines, "new line")}.`}</span>
      <button type="button" className={styles.follow} onClick={onFollow}>
        Jump to the newest
      </button>
    </div>
  );
}
