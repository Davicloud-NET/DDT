// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { removeTokensOf } from "@/tokens/tokens";

import type { ShownPassword } from "./PasswordDialog";
import {
  deleteUser,
  patchUser,
  removeUsers,
  resetPassword,
  resetTwoFactor,
  setUserDisabled,
  upsertUser,
  type CreatedUser,
  type UserView,
} from "./users";
import { shownName } from "./userView";

// The actions that ask before they run.
export type Confirmation = "disable" | "reset-password" | "reset-two-factor" | "delete";

// An action picked from an account's menu.
export type UserAction = "change" | "enable" | Confirmation;

export interface ConfirmationRequest {
  kind: Confirmation;
  user: UserView;
}

export type UserActions = ReturnType<typeof useUserActions>;

// The Users page's actions and the dialogs they open. Every action patches the list with the server's answer. The hub
// brings the same change to other pages.
export function useUserActions() {
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [changing, setChanging] = useState<UserView | null>(null);
  const [confirming, setConfirming] = useState<ConfirmationRequest | null>(null);
  const [password, setPassword] = useState<ShownPassword | null>(null);

  const act = useMutation({
    mutationFn: (request: ConfirmationRequest) => runConfirmed(queryClient, request, setPassword),
    onSuccess: () => {
      setConfirming(null);
    },
  });

  const enable = useMutation({
    mutationFn: (user: UserView) => setUserDisabled(user.id, false),
    onSuccess: (saved) => {
      upsertUser(queryClient, saved);
    },
  });

  function pick(action: UserAction, user: UserView) {
    if (action === "change") {
      setChanging(user);
    } else if (action === "enable") {
      enable.mutate(user);
    } else {
      act.reset();
      setConfirming({ kind: action, user });
    }
  }

  function created(answer: CreatedUser) {
    setAdding(false);
    setPassword({ name: shownName(answer.user), password: answer.password, reset: false });
  }

  function confirm() {
    if (confirming !== null) {
      act.mutate(confirming);
    }
  }

  return {
    act,
    enable,
    adding,
    changing,
    confirming,
    password,
    pick,
    created,
    confirm,
    setAdding,
    setChanging,
    setConfirming,
    setPassword,
  };
}

async function runConfirmed(
  queryClient: QueryClient,
  { kind, user }: ConfirmationRequest,
  showPassword: (shown: ShownPassword) => void,
): Promise<void> {
  switch (kind) {
    case "disable":
      upsertUser(queryClient, await setUserDisabled(user.id, true));
      break;
    case "reset-two-factor":
      upsertUser(queryClient, await resetTwoFactor(user.id));
      break;
    case "reset-password": {
      const reset = await resetPassword(user.id);

      // The answer is only the password. The reset also ends a lockout and makes the account choose a new password.
      patchUser(queryClient, user.id, { mustChangePassword: true, lockedOutUntil: null });
      showPassword({ name: shownName(user), password: reset.password, reset: true });
      break;
    }
    case "delete":
      await deleteUser(user.id);
      removeUsers(queryClient, [user.id]);
      removeTokensOf(queryClient, [user.id]);
      break;
  }
}
