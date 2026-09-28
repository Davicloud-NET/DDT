// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import { useNow } from "@/lib/useNow";

import type { SecretAction } from "../settings";
import {
  DIRECTORY_PROOF_HEADER,
  needsDirectoryProof,
  proofFits,
  type DirectoryProof,
  type LdapSettings,
} from "../signIn";

// Proof from a directory test. When an admin who signs in through the directory changes who can sign in, the save
// must carry this proof, so they can't lock themselves out.
export function useDirectoryProof() {
  const [proof, setProof] = useState<DirectoryProof | null>(null);
  const now = useNow(10_000);

  return {
    setProof,
    // The save checks the real clock, because the ticking one can be seconds behind.
    header: (): Record<string, string> =>
      proof !== null && proof.expires > Date.now() ? { [DIRECTORY_PROOF_HEADER]: proof.token } : {},
    // fresh is the proof while it matches these values and hasn't expired.
    check: (
      signsInThroughDirectory: boolean,
      stored: LdapSettings,
      values: LdapSettings,
      secrets: Record<string, SecretAction>,
    ) => {
      const proofForThese = proof !== null && proofFits(proof, values, secrets);

      return {
        needsProof: needsDirectoryProof(signsInThroughDirectory, stored, values, secrets),
        proofForThese,
        fresh: proofForThese && proof.expires > now ? proof : null,
      };
    },
  };
}
