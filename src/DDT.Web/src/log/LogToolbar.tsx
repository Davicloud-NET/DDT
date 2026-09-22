// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId } from "react";

import { logLevels, type AgentLogLevel } from "./log";

import styles from "./LogToolbar.module.scss";

export type LogScope = "run" | "machine";

export interface LogToolbarProps {
  levels: ReadonlySet<AgentLogLevel>;
  onLevelsChange: (levels: ReadonlySet<AgentLogLevel>) => void;
  search: string;
  onSearchChange: (search: string) => void;
  loadedLines: number;
  // Null when the machine has no run, so only its own lines exist.
  scope: LogScope | null;
  onScopeChange: (scope: LogScope) => void;
  // The step whose lines alone are shown, as "step 4, Apply image".
  step: string | null;
  onClearStep: () => void;
}

export function LogToolbar({
  levels,
  onLevelsChange,
  search,
  onSearchChange,
  loadedLines,
  scope,
  onScopeChange,
  step,
  onClearStep,
}: LogToolbarProps) {
  const searchId = useId();
  const scopeId = useId();

  return (
    <div className={styles.toolbar}>
      <fieldset className={styles.levels}>
        <legend>Levels</legend>
        {logLevels.map((level) => (
          <label key={level}>
            <input
              type="checkbox"
              checked={levels.has(level)}
              onChange={(event) => {
                const next = new Set(levels);

                if (event.target.checked) {
                  next.add(level);
                } else {
                  next.delete(level);
                }

                onLevelsChange(next);
              }}
            />{" "}
            {level}
          </label>
        ))}
      </fieldset>

      <div className={styles.field}>
        <label htmlFor={searchId}>
          Search the {loadedLines.toLocaleString()} loaded {loadedLines === 1 ? "line" : "lines"}
        </label>
        <input
          id={searchId}
          type="search"
          value={search}
          onChange={(event) => {
            onSearchChange(event.target.value);
          }}
        />
      </div>

      {scope !== null && (
        <div className={styles.field}>
          <label htmlFor={scopeId}>Lines</label>
          <select
            id={scopeId}
            value={scope}
            onChange={(event) => {
              onScopeChange(event.target.value === "machine" ? "machine" : "run");
            }}
          >
            <option value="run">Of this run</option>
            <option value="machine">All of this machine, with registration and sign-in</option>
          </select>
        </div>
      )}

      {step !== null && (
        <p className={styles.step}>
          Only the lines of {step}.{" "}
          <button type="button" className={styles.clear} onClick={onClearStep}>
            Show every step
          </button>
        </p>
      )}
    </div>
  );
}
