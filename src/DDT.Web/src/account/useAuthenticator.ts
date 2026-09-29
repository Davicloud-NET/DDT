// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import {
  disableTwoFactor,
  enableTwoFactor,
  regenerateRecoveryCodes,
  startTwoFactorEnrollment,
  type TwoFactorEnrollment,
} from "@/auth/account";
import { currentUserQuery } from "@/auth/auth";

// Turns the second factor on (with a QR code or the key) and off (with a code), and makes new recovery codes. Each
// change patches the signed-in user in the cache with the server's answer.
export function useAuthenticator() {
  const queryClient = useQueryClient();
  const [enrollment, setEnrollment] = useState<TwoFactorEnrollment | null>(null);
  const [code, setCode] = useState("");
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);

  const setEnabled = (twoFactorEnabled: boolean) => {
    queryClient.setQueryData(currentUserQuery.queryKey, (current) =>
      current === undefined || current === null ? current : { ...current, twoFactorEnabled },
    );
  };

  const start = useMutation({
    mutationFn: startTwoFactorEnrollment,
    onSuccess: (started) => {
      setEnrollment(started);
      setCode("");
    },
  });
  const enable = useMutation({
    mutationFn: () => enableTwoFactor(code.trim()),
    onSuccess: (result) => {
      setEnabled(true);
      setEnrollment(null);
      setCode("");
      setRecoveryCodes(result.codes);
    },
  });
  const disable = useMutation({
    mutationFn: () => disableTwoFactor(code.trim()),
    onSuccess: () => {
      setEnabled(false);
      setCode("");
    },
  });
  const regenerate = useMutation({
    mutationFn: regenerateRecoveryCodes,
    onSuccess: (result) => {
      setRecoveryCodes(result.codes);
    },
  });

  const mutations = [start, enable, disable, regenerate];

  return {
    enrollment,
    code,
    setCode,
    recoveryCodes,
    busy: mutations.some((mutation) => mutation.isPending),
    error: mutations.find((mutation) => mutation.isError)?.error ?? null,
    startEnrollment: () => {
      start.mutate();
    },
    cancelEnrollment: () => {
      setEnrollment(null);
      setCode("");
    },
    turnOn: () => {
      enable.mutate();
    },
    turnOff: () => {
      disable.mutate();
    },
    makeRecoveryCodes: () => {
      regenerate.mutate();
    },
    closeRecoveryCodes: () => {
      setRecoveryCodes(null);
    },
  };
}
