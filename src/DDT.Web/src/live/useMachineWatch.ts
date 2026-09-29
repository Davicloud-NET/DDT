// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useContext, useEffect, useEffectEvent } from "react";

import { LiveContext } from "./LiveContext";
import type { LiveStatus } from "./liveConnection";
import type {
  MachineLogAppended,
  MachineWatchHandlers,
  RunStepChanged,
  RunVariablesChanged,
} from "./machineWatches";
import { useLiveStatus } from "./useLiveStatus";

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
  const onRunVariablesChanged = useEffectEvent((event: RunVariablesChanged) => {
    handlers.onRunVariablesChanged?.(event);
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
      onRunVariablesChanged: (event) => {
        onRunVariablesChanged(event);
      },
      onReconnect: () => {
        onReconnect();
      },
    });
  }, [live, machineId]);

  return useLiveStatus();
}
