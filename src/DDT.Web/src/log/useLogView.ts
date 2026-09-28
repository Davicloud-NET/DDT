// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMemo, useState } from "react";

import {
  countByLevel,
  filterLines,
  logLevels,
  type AgentLogLevel,
  type MachineLogEntry,
} from "./log";
import { useMachineLog } from "./useMachineLog";

export type LogScope = "run" | "machine";

// The log panel's state: which lines are loaded, the filters over them, following and the selected line.
export function useLogView({
  machineId,
  runId,
  active,
  stepFilter,
}: {
  machineId: string;
  runId: string | null;
  active: boolean;
  stepFilter: string | null;
}) {
  const [scope, setScope] = useState<LogScope>("run");
  const deploymentId = scope === "run" ? runId : null;
  const log = useMachineLog(machineId, deploymentId, active);
  const [levels, setLevels] = useState<ReadonlySet<string>>(() => new Set(logLevels));
  const [search, setSearch] = useState("");
  const [following, setFollowing] = useState(true);
  const [pausedAt, setPausedAt] = useState<number | null>(null);
  const [selected, setSelected] = useState<MachineLogEntry | null>(null);

  // A new scope loads other lines, so the view starts over at the newest.
  const [shownScope, setShownScope] = useState(deploymentId);

  if (shownScope !== deploymentId) {
    setShownScope(deploymentId);
    setFollowing(true);
    setPausedAt(null);
    setSelected(null);
  }

  const all = log.buffer.lines;
  const lines = useMemo(
    () =>
      filterLines(all, {
        levels: levels as ReadonlySet<AgentLogLevel>,
        search,
        stepId: stepFilter,
      }),
    [all, levels, search, stepFilter],
  );
  const counts = useMemo(() => countByLevel(all), [all]);

  return {
    scope,
    setScope,
    deploymentId,
    log,
    all,
    lines,
    counts,
    levels,
    setLevels,
    search,
    setSearch,
    following,
    // Pausing remembers the newest line shown, so the bar can count the lines that arrive meanwhile.
    setFollowing: (next: boolean) => {
      setFollowing(next);
      setPausedAt(next ? null : (lines.at(-1)?.id ?? null));
    },
    newLines: pausedAt === null ? 0 : lines.filter((line) => line.id > pausedAt).length,
    selected,
    setSelected,
  };
}

export type LogView = ReturnType<typeof useLogView>;
