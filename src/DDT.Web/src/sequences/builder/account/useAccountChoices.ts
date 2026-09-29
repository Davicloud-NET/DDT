// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import type { AccountView } from "@/accounts/accounts";

import type { AccountReference, InputDeclaration } from "../../sequences";
import { useBuilder } from "../builderData";
import { fits, inputFits, keyOf, NONE, type AccountUse } from "./accountChoices";

// A choice of the account list, as its section shows it.
export interface AccountItem {
  key: string;
  id: string;
  text: string;
  description: string | null;
}

// The accounts and Account inputs that fit a use, and how the chosen one is shown.
export function useAccountChoices(
  value: AccountReference | null,
  use: AccountUse,
  noneLabel: string | undefined,
) {
  const { t } = useLingui();
  const { accounts, inputs } = useBuilder();
  const stored = (accounts ?? []).filter((account) => fits(use, account));
  const asked = inputs.filter((input) => inputFits(use, input));
  const key = keyOf(value);
  const accountName = (account: AccountView) => {
    const name = account.name;
    const user = account.userName;

    return t`${name} (${user})`;
  };
  const inputName = (input: InputDeclaration) => {
    const name = input.label.trim() === "" ? input.name : input.label;

    return t`${name}, asked for the run`;
  };

  const chosenAccount = stored.find((account) => account.id === value?.accountId);
  const chosenInput = asked.find(
    (input) => value?.accountId === null && input.name === value.input,
  );
  const lostInput = value?.input ?? "";
  // A choice that's no longer in the lists, such as a deleted account, is still shown.
  const missing =
    key === NONE || chosenAccount !== undefined || chosenInput !== undefined
      ? null
      : value?.accountId !== null && value?.accountId !== undefined
        ? t`An account this server does not have`
        : t`${lostInput}, an input this sequence does not declare`;
  const chosenLabel =
    chosenAccount !== undefined
      ? accountName(chosenAccount)
      : chosenInput !== undefined
        ? inputName(chosenInput)
        : (missing ?? noneLabel ?? t`None`);
  const storedItems: AccountItem[] = stored.map((account) => ({
    key: account.id,
    id: `account:${account.id}`,
    text: accountName(account),
    description: account.domain,
  }));
  const askedItems: AccountItem[] = asked.map((input) => ({
    key: input.name,
    id: `input:${input.name}`,
    text: inputName(input),
    description: null,
  }));

  return { key, missing, chosenLabel, noneStored: accounts === null, storedItems, askedItems };
}
