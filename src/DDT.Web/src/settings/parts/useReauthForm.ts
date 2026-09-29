// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { useState } from "react";

import { reauthenticate } from "../settings";

// The password and authenticator code typed again, and the server's check of them. Neither outlives the dialog.
export function useReauthForm(onAccepted: () => void, onCancel: () => void) {
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const close = () => {
    setPassword("");
    setCode("");
    setError(null);
    onCancel();
  };

  const submit = () => {
    setBusy(true);
    setError(null);
    reauthenticate(password, code.trim() === "" ? null : code.trim())
      .then(() => {
        setPassword("");
        setCode("");
        onAccepted();
      })
      .catch((failure: unknown) => {
        setError(failure instanceof Error ? failure.message : t`That did not work.`);
      })
      .finally(() => {
        setBusy(false);
      });
  };

  return { password, setPassword, code, setCode, error, busy, close, submit };
}
