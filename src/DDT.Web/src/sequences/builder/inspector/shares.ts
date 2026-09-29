// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { AccountView } from "@/accounts/accounts";

import type { AccountReference, InputDeclaration } from "../../sequences";

// A share is connected for one step, and a step connects at most this many.
export const MAX_SHARES = 4;

// A new share's account: the first stored account, or else the sequence's first Account input.
export function firstShareAccount(
  accounts: readonly AccountView[] | null,
  inputs: readonly InputDeclaration[],
): AccountReference {
  return accounts?.[0] !== undefined
    ? { accountId: accounts[0].id, input: null }
    : { accountId: null, input: inputs.find((input) => input.kind === "Account")?.name ?? null };
}
