// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";

import { saveFile } from "@/lib/saveFile";

import {
  autopilotCsvName,
  downloadAutopilotCsv,
  markUploadPending,
  requestAutopilotUpload,
} from "./autopilot";

// The CSV download for some machines, and an upload asked for again for one.
export function useAutopilotActions() {
  const queryClient = useQueryClient();

  const download = useMutation({
    mutationFn: (machineIds: readonly string[]) => downloadAutopilotCsv(machineIds),
    onSuccess: (csv) => {
      saveFile(csv, autopilotCsvName);
    },
  });

  const upload = useMutation({
    mutationFn: (machineId: string) => requestAutopilotUpload(machineId),
    onSuccess: (_, machineId) => {
      markUploadPending(queryClient, machineId);
    },
  });

  return { download, upload };
}
