// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { AccountView } from "@/accounts/accounts";

import type { AccountReference, InputDeclaration } from "../../sequences";

// What an account is for: running a script as it, joining the domain with it, or connecting a share with it. Only
// accounts and Account inputs bound to that destination are offered; the server checks the rest when the step runs.
export type AccountUse = "runAs" | "join" | "share";

export const NONE = "none";

// The key of a choice in the account list: none, a stored account or an Account input.
export function keyOf(reference: AccountReference | null): string {
  if (reference === null || (reference.accountId === null && reference.input === null)) {
    return NONE;
  }

  return reference.accountId !== null
    ? `account:${reference.accountId}`
    : `input:${reference.input ?? ""}`;
}

export function referenceOf(key: string): AccountReference | null {
  return key === NONE
    ? null
    : key.startsWith("account:")
      ? { accountId: key.slice("account:".length), input: null }
      : { accountId: null, input: key.slice("input:".length) };
}

export function fits(use: AccountUse, account: AccountView): boolean {
  return use === "runAs" ? account.runAs : use === "join" ? account.domain !== null : true;
}

export function inputFits(use: AccountUse, input: InputDeclaration): boolean {
  if (input.kind !== "Account") {
    return false;
  }

  const destination = input.account;

  return use === "runAs"
    ? destination?.runAs === true
    : use === "join"
      ? (destination?.domain ?? null) !== null
      : true;
}
