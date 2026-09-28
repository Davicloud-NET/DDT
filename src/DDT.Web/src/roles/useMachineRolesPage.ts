// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";
import { rulesQuery } from "@/rules/rules";

import { machineRolesQuery, type MachineRoleView } from "./roles";

// The machine roles page's lists, which the hub keeps current, and which role's drawer or deletion is open.
export function useMachineRolesPage() {
  const freshness = liveListOptions(useLiveStatus());
  const roles = useQuery({ ...machineRolesQuery, ...freshness });
  const rules = useQuery({ ...rulesQuery, ...freshness });
  const canEdit = useIsAdministrator();
  const mark = useLiveMarks({
    queryKey: machineRolesQuery.queryKey,
    items: (list) => list,
    id: (role) => role.id,
    signature: (role) => String(role.revision),
    tone: () => "idle",
  });

  // The drawer's role, null for a new one; key opens a fresh form each time.
  const [drawer, setDrawer] = useState<{ key: number; role: MachineRoleView | null } | null>(null);
  const [deleting, setDeleting] = useState<MachineRoleView | null>(null);

  return {
    roles,
    list: roles.data ?? [],
    ruleList: rules.data ?? [],
    canEdit,
    mark,
    drawer,
    deleting,
    open: (role: MachineRoleView | null) => {
      setDrawer((current) => ({ key: (current?.key ?? 0) + 1, role }));
    },
    close: () => {
      setDrawer(null);
    },
    setDeleting,
  };
}
