// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";

import { deploymentOptionsQuery } from "./deployments";

// Milliseconds to add to this browser's clock to get the server's. 0 until the server has said its time, and while
// nothing wants it: the server is asked only for a page that holds one of its times against a limit.
export function useServerClockOffset(wanted: boolean): number {
  return useQuery({ ...deploymentOptionsQuery, enabled: wanted }).data?.serverClockOffsetMs ?? 0;
}
