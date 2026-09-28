// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { formatBytes } from "@/lib/format";

import { useGuardedAction } from "../useGuardedAction";

import type { Binary } from "./binary";

// A chosen file is checked and confirmed first. askFirst asks for the password before the large body is sent.
export function useBinaryUpload(binary: Binary) {
  const queryClient = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [uploaded, setUploaded] = useState<string | null>(null);

  const action = useGuardedAction({
    askFirst: true,
    send: () => {
      if (file === null) {
        throw new Error(binary.chooseFirst());
      }

      return binary.upload(file);
    },
    onDone: (answer) => {
      queryClient.setQueryData(binary.query.queryKey, answer);
      setUploaded(answer.sha256);
      setFile(null);
    },
  });

  const pick = (chosen: File | undefined) => {
    if (chosen === undefined || action.busy) {
      return;
    }

    const name = chosen.name;

    action.reset();
    setUploaded(null);
    setProblem(null);

    if (chosen.size === 0) {
      setProblem(t`${name} is empty.`);
    } else if (chosen.size > binary.maxBytes) {
      setProblem(binary.tooLarge(name, formatBytes(binary.maxBytes)));
    } else {
      setFile(chosen);
      setConfirming(true);
    }
  };

  return {
    action,
    name: file?.name ?? "",
    size: file === null ? "" : formatBytes(file.size),
    confirming,
    problem,
    uploaded,
    pick,
    confirm: () => {
      setConfirming(false);
      action.start();
    },
    dismiss: () => {
      setConfirming(false);
      setFile(null);
    },
    cancelReauth: () => {
      action.cancelReauth();
      setFile(null);
    },
  };
}
