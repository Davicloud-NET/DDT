// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { deleteRecoveryKey, dropRecoveryKey, revealRecoveryKey } from "./machineRecords";

// An administrator's reveal and delete. A revealed password stays in this hook's state only, so it is gone once the
// page is left.
export function useRecoveryKeyActions(machineId: string) {
  const queryClient = useQueryClient();
  const [revealed, setRevealed] = useState<Record<string, string>>({});
  const [deleteOn, setDeleteOn] = useState<string | null>(null);

  const reveal = useMutation({
    mutationFn: (keyId: string) => revealRecoveryKey(machineId, keyId),
    onSuccess: (password, keyId) => {
      setRevealed((shown) => ({ ...shown, [keyId]: password }));
    },
  });

  const remove = useMutation({
    mutationFn: (keyId: string) => deleteRecoveryKey(machineId, keyId),
    onSuccess: (_, keyId) => {
      dropRecoveryKey(queryClient, machineId, keyId);
      setDeleteOn(null);
    },
  });

  return { revealed, reveal, remove, deleteOn, setDeleteOn };
}

export type RecoveryKeyActions = ReturnType<typeof useRecoveryKeyActions>;
