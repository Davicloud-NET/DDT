// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { changePassword } from "@/auth/account";
import { currentUserQuery, type CurrentUser } from "@/auth/auth";

// The password form's fields and the change. A successful change clears mustChangePassword on the server. The
// cached account is patched to match, which unlocks the rest of DDT.
export function usePasswordChange(user: CurrentUser) {
  const queryClient = useQueryClient();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [again, setAgain] = useState("");
  const [mismatch, setMismatch] = useState(false);
  const [changed, setChanged] = useState<"no" | "yes" | "unlocked">("no");

  const change = useMutation({
    mutationFn: () => changePassword(current, next),
    onSuccess: () => {
      setCurrent("");
      setNext("");
      setAgain("");
      setChanged(user.mustChangePassword ? "unlocked" : "yes");
      queryClient.setQueryData(currentUserQuery.queryKey, (cached) =>
        cached === undefined || cached === null ? cached : { ...cached, mustChangePassword: false },
      );
    },
  });

  function submit() {
    setMismatch(next !== again);
    setChanged("no");

    if (next === again) {
      change.mutate();
    }
  }

  function changeAgain(value: string) {
    setAgain(value);
    setMismatch(false);
  }

  return {
    current,
    setCurrent,
    next,
    setNext,
    again,
    changeAgain,
    mismatch,
    changed,
    change,
    submit,
  };
}
