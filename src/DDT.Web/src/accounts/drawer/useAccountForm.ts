// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { ApiError } from "@/lib/api";
import { conflictOf, noFindings, otherRefusal, refusalFindings, unplaced } from "@/rules/refusals";
import { fieldFindings, type Findings } from "@/sequences/problems";

import {
  accountEditOf,
  accountRequestOf,
  createAccount,
  putAccount,
  reachesNewDestination,
  saveAccount,
  wantsReauthentication,
  type AccountEdit,
  type AccountView,
} from "../accounts";

function isShown(field: string): boolean {
  return /^(name|userName|domain|hosts|runAs|password)(\.|\[|$)/.test(field);
}

interface AccountFormOptions {
  // Null for a new account.
  account: AccountView | null;
  onClose: () => void;
}

export type AccountForm = ReturnType<typeof useAccountForm>;

// Holds the edits to an account and saves them. The server only accepts the save after the person entered their
// password again.
export function useAccountForm({ account, onClose }: AccountFormOptions) {
  const queryClient = useQueryClient();
  const [base, setBase] = useState<AccountView | null>(account);
  const [edit, setEdit] = useState<AccountEdit>(() => accountEditOf(account));
  const [findings, setFindings] = useState<Findings>(noFindings);
  const [theirs, setTheirs] = useState<AccountView | null>(null);
  const [gone, setGone] = useState(false);
  // The revision to save with again once the password is entered.
  const [reauth, setReauth] = useState<number | null>(null);

  const change = (patch: Partial<AccountEdit>) => {
    setEdit((current) => ({ ...current, ...patch }));
  };

  const save = useMutation({
    mutationFn: (revision: number) =>
      base === null
        ? createAccount(accountRequestOf(0, edit))
        : saveAccount(base.id, accountRequestOf(revision, edit)),
    onSuccess: (saved) => {
      putAccount(queryClient, saved);
      onClose();
    },
    onError: (error, revision) => {
      if (wantsReauthentication(error)) {
        setReauth(revision);
        return;
      }

      const current = conflictOf(error) as AccountView | null;

      if (current !== null) {
        putAccount(queryClient, current);
        setTheirs(current);
        return;
      }

      if (error instanceof ApiError && error.status === 404) {
        setGone(true);
        return;
      }

      const refused = refusalFindings(error) ?? noFindings;

      setFindings(refused);

      // The server wants the password typed again, so the field for it opens.
      if (
        fieldFindings(refused, "password").problems.length > 0 &&
        edit.password.action !== "Set"
      ) {
        change({ password: { action: "Set", value: "" } });
      }
    },
  });

  const takeTheirs = () => {
    if (theirs === null) {
      return;
    }

    setBase(theirs);
    setEdit(accountEditOf(theirs));
    setFindings(noFindings);
    setTheirs(null);
    save.reset();
  };

  const keepMine = () => {
    if (theirs === null) {
      return;
    }

    setBase(theirs);
    save.mutate(theirs.revision);
  };

  return {
    base,
    edit,
    findings,
    theirs,
    gone,
    change,
    busy: save.isPending,
    loose: unplaced(findings, isShown),
    refused:
      save.isError && !wantsReauthentication(save.error) ? otherRefusal(save.error, gone) : null,
    // The user name, the domain or a server changed, so a stored password has to be entered again.
    moved: base !== null && base.password.isSet && reachesNewDestination(base, edit),
    submit: () => {
      save.mutate(base?.revision ?? 0);
    },
    takeTheirs,
    keepMine,
    needsReauth: reauth !== null,
    resendAfterReauth: () => {
      const revision = reauth ?? 0;

      setReauth(null);
      save.mutate(revision);
    },
    cancelReauth: () => {
      setReauth(null);
      save.reset();
    },
  };
}
