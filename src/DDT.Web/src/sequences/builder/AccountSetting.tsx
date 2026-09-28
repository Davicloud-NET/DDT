// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { useContext, type ReactNode } from "react";
import { Header, ListBoxSection } from "react-aria-components";

import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import { EditorLock } from "../editorLock";
import { fieldFindings, type Findings } from "../problems";
import type { AccountReference, InputDeclaration } from "../sequences";
import { useBuilder, type AccountView } from "./builderData";

// What an account is for: running a script as it, joining the domain with it, or connecting a share with it. Only
// accounts and Account inputs bound to that destination are offered; the server checks the rest when the step runs.
export type AccountUse = "runAs" | "join" | "share";

const NONE = "none";

function keyOf(reference: AccountReference | null): string {
  if (reference === null || (reference.accountId === null && reference.input === null)) {
    return NONE;
  }

  return reference.accountId !== null
    ? `account:${reference.accountId}`
    : `input:${reference.input ?? ""}`;
}

function fits(use: AccountUse, account: AccountView): boolean {
  return use === "runAs" ? account.runAs : use === "join" ? account.domain !== null : true;
}

function inputFits(use: AccountUse, input: InputDeclaration): boolean {
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

// Chooses the account a step uses: one stored on the server, or an Account input of this sequence that is asked for
// the run. noneLabel names the choice of no account where the step may have none.
export function AccountSetting({
  label,
  field,
  findings,
  hint,
  value,
  use,
  noneLabel,
  onChange,
  className,
}: {
  label: ReactNode;
  field: string;
  findings: Findings;
  hint?: ReactNode;
  value: AccountReference | null;
  use: AccountUse;
  noneLabel?: string;
  onChange: (value: AccountReference | null) => void;
  className?: string;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const { accounts, inputs } = useBuilder();
  const { problems, warnings } = fieldFindings(findings, field);
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
  // A choice the lists do not have any more, such as an account deleted since, is still shown as it is.
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
  const described = [
    hint,
    accounts === null && !locked ? t`No account is stored on the server yet.` : null,
    ...warnings,
  ].filter((part) => part !== null && part !== undefined);
  const description =
    described.length === 0 ? undefined : (
      <>
        {described.map((part, index) => (
          <span key={index} className={index === 0 && hint !== undefined ? undefined : "block"}>
            {part}{" "}
          </span>
        ))}
      </>
    );

  if (locked) {
    return (
      <div data-field={field} className={className}>
        <TextField
          label={label}
          value={chosenLabel}
          isReadOnly
          isInvalid={problems.length > 0}
          errorMessage={problems.join(" ")}
          {...(description === undefined ? {} : { hint: description })}
        />
      </div>
    );
  }

  return (
    <div data-field={field} className={className}>
      <Select
        label={label}
        value={key === NONE && noneLabel === undefined ? null : key}
        placeholder={t`Choose an account`}
        isInvalid={problems.length > 0}
        errorMessage={problems.join(" ")}
        {...(description === undefined ? {} : { hint: description })}
        onChange={(chosen) => {
          const text = String(chosen);

          if (text === key) {
            return;
          }

          onChange(
            text === NONE
              ? null
              : text.startsWith("account:")
                ? { accountId: text.slice("account:".length), input: null }
                : { accountId: null, input: text.slice("input:".length) },
          );
        }}
      >
        {noneLabel === undefined ? null : (
          <ListBoxItem id={NONE} textValue={noneLabel}>
            {noneLabel}
          </ListBoxItem>
        )}
        {missing === null ? null : (
          <ListBoxItem id={key} textValue={missing}>
            {missing}
          </ListBoxItem>
        )}
        {stored.length > 0 ? (
          <ListBoxSection>
            <Header className="px-2.5 pt-2 pb-1 type-small text-muted">{t`Stored accounts`}</Header>
            {stored.map((account) => {
              const text = accountName(account);

              return (
                <ListBoxItem
                  key={account.id}
                  id={`account:${account.id}`}
                  textValue={text}
                  {...(account.domain === null ? {} : { description: account.domain })}
                >
                  {text}
                </ListBoxItem>
              );
            })}
          </ListBoxSection>
        ) : null}
        {asked.length > 0 ? (
          <ListBoxSection>
            <Header className="px-2.5 pt-2 pb-1 type-small text-muted">
              {t`Asked for the run`}
            </Header>
            {asked.map((input) => {
              const text = inputName(input);

              return (
                <ListBoxItem key={input.name} id={`input:${input.name}`} textValue={text}>
                  {text}
                </ListBoxItem>
              );
            })}
          </ListBoxSection>
        ) : null}
      </Select>
    </div>
  );
}
