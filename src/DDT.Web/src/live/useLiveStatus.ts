// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useContext, useSyncExternalStore } from "react";

import { LiveContext } from "./LiveContext";
import type { LiveStatus } from "./liveConnection";

function subscribeNothing(): () => void {
  return () => undefined;
}

function offline(): LiveStatus {
  return "offline";
}

// Whether changes arrive by push. Lists read again on a timer only while they do not.
export function useLiveStatus(): LiveStatus {
  const live = useContext(LiveContext);

  return useSyncExternalStore(
    live ? live.onStatusChange : subscribeNothing,
    live ? live.status : offline,
  );
}
