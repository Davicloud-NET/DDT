// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation } from "@tanstack/react-query";
import { useState } from "react";

import type { CurrentUser } from "@/auth/auth";

import type { SecretAction } from "../settings";
import {
  DIRECTORY_PROOF_LIFETIME_MS,
  testLdap,
  type DirectoryProof,
  type LdapSettings,
} from "../signIn";

// The user a directory test signs in, and the test. A proof in its answer goes to onProof with the values it was
// given for.
export function useDirectoryTest(me: CurrentUser, onProof: (proof: DirectoryProof) => void) {
  const [userName, setUserName] = useState(me.source === "Directory" ? me.userName : "");
  const [password, setPassword] = useState("");

  const test = useMutation({
    mutationFn: (tested: { values: LdapSettings; secrets: Record<string, SecretAction> }) =>
      testLdap({
        ...tested,
        userName: userName.trim() === "" ? null : userName.trim(),
        password: password === "" ? null : password,
      }),
    onSuccess: (result, tested) => {
      setPassword("");

      if (result.proof !== null) {
        onProof({
          token: result.proof,
          values: tested.values,
          bindPassword: tested.secrets.bindPassword ?? { action: "Keep" },
          expires: Date.now() + DIRECTORY_PROOF_LIFETIME_MS,
        });
      }
    },
  });

  return { userName, setUserName, password, setPassword, test };
}
