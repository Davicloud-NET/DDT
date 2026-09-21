// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useContext, useEffect, useEffectEvent, useSyncExternalStore } from "react";

import { LiveContext } from "./LiveContext";
import type {
  LiveStatus,
  MachineLogAppended,
  MachineWatchHandlers,
  RunStepChanged,
} from "./liveConnection";

function subscribeNothing(): () => void {
  return () => undefined;
}

function offline(): LiveStatus {
  return "offline";
}

// Receives the events of one machine while mounted, and says whether they arrive. The handlers may change
// on every render.
export function useMachineWatch(machineId: string, handlers: MachineWatchHandlers): LiveStatus {
  // The router tests mock the shell's connection away, which leaves undefined here, so this checks
  // for any missing connection.
  const live = useContext(LiveContext);

  const onLogAppended = useEffectEvent((event: MachineLogAppended) => {
    handlers.onLogAppended?.(event);
  });
  const onRunStepChanged = useEffectEvent((event: RunStepChanged) => {
    handlers.onRunStepChanged?.(event);
  });
  const onReconnect = useEffectEvent(() => {
    handlers.onReconnect?.();
  });

  useEffect(() => {
    if (!live) {
      return;
    }

    return live.watchMachine(machineId, {
      onLogAppended: (event) => {
        onLogAppended(event);
      },
      onRunStepChanged: (event) => {
        onRunStepChanged(event);
      },
      onReconnect: () => {
        onReconnect();
      },
    });
  }, [live, machineId]);

  return useSyncExternalStore(
    live ? live.onStatusChange : subscribeNothing,
    live ? live.status : offline,
  );
}
