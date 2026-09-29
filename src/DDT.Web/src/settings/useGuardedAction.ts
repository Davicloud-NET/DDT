// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation } from "@tanstack/react-query";
import { useState } from "react";

import {
  reauthenticationToken,
  refusalOf,
  type SaveRefusal,
  type SettingsFinding,
} from "./settings";

// An action outside a section's save that needs a fresh proof of identity, and sometimes confirmed warnings, such as a
// certificate from a new root. It asks for what the server wants and sends the action again. With askFirst it asks for
// the password first when no proof is held, so a large body isn't sent just to be refused unread.
export function useGuardedAction<T>({
  send,
  onDone,
  askFirst = false,
}: {
  send: (confirm: string[]) => Promise<T>;
  onDone: (answer: T) => void;
  askFirst?: boolean;
}) {
  const [needsReauth, setNeedsReauth] = useState(false);
  const [warnings, setWarnings] = useState<SettingsFinding[] | null>(null);
  // The warnings this action has confirmed. The resend after the password includes them again.
  const [confirmed, setConfirmed] = useState<string[]>([]);
  const [refusal, setRefusal] = useState<SaveRefusal | null>(null);

  const action = useMutation({
    mutationFn: send,
    onSuccess: (answer) => {
      setWarnings(null);
      setConfirmed([]);
      setRefusal(null);
      onDone(answer);
    },
    onError: (error) => {
      const refused = refusalOf(error);

      if (refused.kind === "reauthenticate") {
        setWarnings(null);
        setNeedsReauth(true);
      } else if (
        refused.kind === "invalid" &&
        refused.confirm.length > 0 &&
        Object.keys(refused.fields).length === 0
      ) {
        setWarnings(refused.confirm);
      } else {
        setWarnings(null);
        setRefusal(refused);
      }
    },
  });

  return {
    busy: action.isPending,
    needsReauth,
    warnings,
    refusal,
    // The refusal as one sentence, whatever kind it is.
    error:
      refusal === null
        ? null
        : refusal.kind === "invalid"
          ? Object.values(refusal.fields).flat().join(" ")
          : refusal.message,
    start: () => {
      setConfirmed([]);
      setRefusal(null);

      if (askFirst && reauthenticationToken() === null) {
        setNeedsReauth(true);
      } else {
        action.mutate([]);
      }
    },
    retryAfterReauth: () => {
      setNeedsReauth(false);
      action.mutate(confirmed);
    },
    cancelReauth: () => {
      setNeedsReauth(false);
    },
    confirmWarnings: () => {
      const codes = [
        ...confirmed,
        ...(warnings ?? [])
          .map((warning) => warning.code)
          .filter((code): code is string => code !== null),
      ];

      setConfirmed(codes);
      setWarnings(null);
      action.mutate(codes);
    },
    cancelWarnings: () => {
      setWarnings(null);
    },
    reset: () => {
      setRefusal(null);
      setWarnings(null);
      setConfirmed([]);
    },
  };
}

export type GuardedAction = ReturnType<typeof useGuardedAction>;
