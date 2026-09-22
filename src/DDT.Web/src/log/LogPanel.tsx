// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMemo, useState } from "react";

import type { DeploymentStepView } from "@/deployments/deployments";

import {
  filterLines,
  logLevels,
  STORED_LINES_PER_MACHINE,
  type AgentLogLevel,
  type MachineLogEntry,
} from "./log";
import { MAX_BUFFERED_LINES } from "./logBuffer";
import { LogFollowBar } from "./LogFollowBar";
import { LogLineDetail } from "./LogLineDetail";
import { LogToolbar, type LogScope } from "./LogToolbar";
import { LogViewport } from "./LogViewport";
import { LOG_POLL_MS, useMachineLog } from "./useMachineLog";

import styles from "./LogPanel.module.scss";

export interface LogPanelProps {
  machineId: string;
  // The run the page shows, whose lines the panel shows first.
  runId: string | null;
  active: boolean;
  steps: readonly DeploymentStepView[];
  // Only this step's lines are shown.
  stepFilter: string | null;
  onStepFilterChange: (stepId: string | null) => void;
}

function stepLabel(steps: readonly DeploymentStepView[], stepId: string | null): string | null {
  const step = steps.find((candidate) => candidate.stepId === stepId);

  return step === undefined ? null : `step ${String(step.index + 1)}, ${step.name}`;
}

export function LogPanel({
  machineId,
  runId,
  active,
  steps,
  stepFilter,
  onStepFilterChange,
}: LogPanelProps) {
  const [scope, setScope] = useState<LogScope>("run");
  const deploymentId = scope === "run" ? runId : null;
  const log = useMachineLog(machineId, deploymentId, active);

  const [levels, setLevels] = useState<ReadonlySet<AgentLogLevel>>(() => new Set(logLevels));
  const [search, setSearch] = useState("");
  const [following, setFollowing] = useState(true);
  const [pausedAt, setPausedAt] = useState<number | null>(null);
  const [selected, setSelected] = useState<MachineLogEntry | null>(null);

  // Other lines replace the loaded ones, so the view starts again at the newest.
  const [shownScope, setShownScope] = useState(deploymentId);
  if (shownScope !== deploymentId) {
    setShownScope(deploymentId);
    setFollowing(true);
    setPausedAt(null);
    setSelected(null);
  }

  const all = log.buffer.lines;
  const lines = useMemo(
    () => filterLines(all, { levels, search, stepId: stepFilter }),
    [all, levels, search, stepFilter],
  );
  const newLines = pausedAt === null ? 0 : lines.filter((line) => line.id > pausedAt).length;
  const full = all.length >= MAX_BUFFERED_LINES;

  return (
    <section className={styles.panel} aria-label="Log">
      <h2>Log</h2>

      <LogToolbar
        levels={levels}
        onLevelsChange={setLevels}
        search={search}
        onSearchChange={setSearch}
        loadedLines={all.length}
        scope={runId === null ? null : scope}
        onScopeChange={setScope}
        step={stepLabel(steps, stepFilter)}
        onClearStep={() => {
          onStepFilterChange(null);
        }}
      />

      <p className={styles.secondary}>
        {log.polling
          ? `Polling every ${String(LOG_POLL_MS / 1000)} s, because the live connection is down.`
          : log.status === "live"
            ? "New lines appear as the agent sends them."
            : log.status === "reconnecting"
              ? "Reconnecting. Lines sent meanwhile are read once the connection is back."
              : "The live connection is down. New lines appear once it is back."}
      </p>

      {log.error !== null && <p className={styles.error}>The log could not be read: {log.error}</p>}

      {log.buffer.hasOlder && !full && (
        <div>
          <button
            type="button"
            className={styles.button}
            disabled={log.loadingOlder}
            onClick={log.loadOlder}
          >
            {log.loadingOlder ? "Loading older lines" : "Load older lines"}
          </button>
        </div>
      )}
      {log.buffer.hasOlder && full && (
        <p className={styles.secondary}>
          This page holds at most {MAX_BUFFERED_LINES.toLocaleString()} lines. Older ones stay on
          the server.
        </p>
      )}
      {log.loaded && !log.buffer.hasOlder && all.length > 0 && (
        <p className={styles.secondary}>
          Start of the stored log. The server keeps the newest{" "}
          {STORED_LINES_PER_MACHINE.toLocaleString()} lines of each machine.
        </p>
      )}

      {log.loaded && all.length === 0 && log.error === null && (
        <p className={styles.secondary}>
          {deploymentId === null
            ? "This machine has sent no log lines yet."
            : "No log lines for this run yet. The agent sends its log while it runs."}
        </p>
      )}
      {all.length > 0 && lines.length === 0 && (
        <p className={styles.secondary}>No loaded line matches the filter.</p>
      )}

      <LogViewport
        lines={lines}
        following={following}
        onFollowingChange={(next) => {
          setFollowing(next);
          setPausedAt(next ? null : (lines.at(-1)?.id ?? null));
        }}
        selectedId={selected?.id ?? null}
        onSelect={setSelected}
      />

      {!following && (
        <LogFollowBar
          newLines={newLines}
          onFollow={() => {
            setFollowing(true);
            setPausedAt(null);
          }}
        />
      )}

      {selected !== null && (
        <LogLineDetail
          line={selected}
          step={stepLabel(steps, selected.stepId)}
          onClose={() => {
            setSelected(null);
          }}
        />
      )}
    </section>
  );
}
