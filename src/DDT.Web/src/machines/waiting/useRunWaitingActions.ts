// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { putRun } from "@/deployments/deployments";
import {
  answerInputs,
  continueRun,
  type ContinueRunRequest,
  type RunAnswer,
} from "@/deployments/waitingRun";
import type { InputAnswer } from "@/inputs/inputs";
import { showToast } from "@/ui/toasts";

// Continues a waiting run or gives its answers. The server answers with the run as it is now, which goes into the
// cache unchanged. If the action came too late, the answer says so.
export function useRunWaitingActions(machineId: string) {
  const queryClient = useQueryClient();
  const [asking, setAsking] = useState(false);

  const settle = (answer: RunAnswer, late: () => void) => {
    putRun(queryClient, answer.view);

    if (answer.late) {
      late();
    }
  };

  const proceed = useMutation({
    mutationFn: (request: ContinueRunRequest) => continueRun(machineId, request),
    onSuccess: (answer) => {
      settle(answer, () => {
        showToast({
          title: t`The run went on already`,
          description: t`Someone at the machine or on another page let it go on first.`,
        });
      });
    },
  });

  const answer = useMutation({
    mutationFn: (answers: InputAnswer[]) => answerInputs(machineId, answers),
    onSuccess: (result) => {
      setAsking(false);
      settle(result, () => {
        showToast({
          title: t`The run has its answers already`,
          description: t`They were given at the machine or on another page first.`,
        });
      });
    },
  });

  return { asking, setAsking, proceed, answer };
}
