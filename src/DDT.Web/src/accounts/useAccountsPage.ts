// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";

import { accountsQuery, type AccountView } from "./accounts";

// The accounts page's list, which the hub keeps current, and which account's drawer or deletion is open.
export function useAccountsPage() {
  const accounts = useQuery({ ...accountsQuery, ...liveListOptions(useLiveStatus()) });
  const canEdit = useIsAdministrator();
  const mark = useLiveMarks({
    queryKey: accountsQuery.queryKey,
    items: (list) => list,
    id: (account) => account.id,
    signature: (account) =>
      `${String(account.revision)} ${String(account.password.isSet)} ${String(account.usedBy.length)}`,
    tone: () => "idle",
  });

  // The drawer's account, null for a new one; key opens a fresh form each time.
  const [drawer, setDrawer] = useState<{ key: number; account: AccountView | null } | null>(null);
  const [deleting, setDeleting] = useState<AccountView | null>(null);

  return {
    accounts,
    list: accounts.data ?? [],
    canEdit,
    mark,
    drawer,
    deleting,
    open: (account: AccountView | null) => {
      setDrawer((current) => ({ key: (current?.key ?? 0) + 1, account }));
    },
    close: () => {
      setDrawer(null);
    },
    setDeleting,
  };
}
