// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { Button } from "@/ui/Button";
import { ConfirmDialog } from "@/ui/ConfirmDialog";
import { Notice } from "@/ui/Notice";
import { ProgressBar } from "@/ui/ProgressBar";

import type { AgentBinaryView } from "../agentBinary";
import { useGuardedAction } from "../useGuardedAction";

import type { Binary } from "./binary";

// Takes an upload away again, so machines get what the server came with. It says so when that one is the newer:
// an upgrade brought it, and the upload still hides it.
export function UploadRemoval({ binary, view }: { binary: Binary; view: AgentBinaryView }) {
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState(false);
  const [removed, setRemoved] = useState(false);
  const removal = useGuardedAction({
    send: () => binary.remove(),
    onDone: (answer) => {
      queryClient.setQueryData(binary.query.queryKey, answer);
      setRemoved(true);
    },
  });
  const reason = removal.error;

  if (view.source !== "Uploaded") {
    return removed ? <Notice>{binary.removed}</Notice> : null;
  }

  const ask = () => {
    removal.reset();
    setConfirming(true);
  };

  return (
    <>
      {removal.busy ? (
        <ProgressBar label={binary.removing} />
      ) : view.newerBundledVersion === null ? (
        <div>
          <Button variant="quiet" onPress={ask}>
            {binary.removeLabel}
          </Button>
        </div>
      ) : (
        <Notice
          tone="attention"
          actions={
            <Button size="sm" onPress={ask}>
              {binary.removeLabel}
            </Button>
          }
        >
          {binary.newer(view.newerBundledVersion)}
        </Notice>
      )}
      {reason === null ? null : <Notice tone="fail">{binary.notRemoved(reason)}</Notice>}

      <ConfirmDialog
        isOpen={confirming}
        onOpenChange={setConfirming}
        title={binary.removeTitle}
        confirmLabel={binary.removeLabel}
        onConfirm={() => {
          setConfirming(false);
          removal.start();
        }}
      >
        <p>{binary.removeBody}</p>
      </ConfirmDialog>
    </>
  );
}
