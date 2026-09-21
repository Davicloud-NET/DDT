// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo } from "react";

import { createLiveConnection, type LiveConnection } from "./liveConnection";

// Keeps the application's live connection up while the shell is shown. The shell hands it to the pages
// through LiveContext.
export function useLiveUpdates(): LiveConnection {
  const queryClient = useQueryClient();
  const live = useMemo(() => createLiveConnection(queryClient), [queryClient]);

  useEffect(() => {
    live.start();

    return () => {
      live.stop();
    };
  }, [live]);

  return live;
}
