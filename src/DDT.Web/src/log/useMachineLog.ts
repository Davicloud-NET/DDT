// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useMemo, useSyncExternalStore } from "react";

import { useMachineWatch } from "@/live/useMachineWatch";

import { newestId } from "./logBuffer";
import { createLogReader } from "./logReader";

// Without the live connection the log is read this often while a run is active.
export const LOG_POLL_MS = 5_000;

// A machine's log, or one run's. Reads the newest lines on open, then every line the server says it
// received, and whatever was missed while the live connection was down.
export function useMachineLog(machineId: string, deploymentId: string | null, active: boolean) {
  const reader = useMemo(() => createLogReader(machineId, deploymentId), [machineId, deploymentId]);
  const snapshot = useSyncExternalStore(reader.subscribe, reader.snapshot);

  useEffect(() => {
    reader.start();

    return () => {
      reader.close();
    };
  }, [reader]);

  const status = useMachineWatch(machineId, {
    onLogAppended: (event) => {
      if (event.lastLineId > (newestId(reader.snapshot().buffer) ?? 0)) {
        reader.catchUp();
      }
    },
    onReconnect: () => {
      reader.catchUp();
    },
  });

  const polling = status !== "live" && active;

  useEffect(() => {
    if (!polling) {
      return;
    }

    const timer = window.setInterval(() => {
      reader.catchUp();
    }, LOG_POLL_MS);

    return () => {
      window.clearInterval(timer);
    };
  }, [polling, reader]);

  return { ...snapshot, status, polling, loadOlder: reader.loadOlder };
}
