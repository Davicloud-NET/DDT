// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";

import { currentUserQuery } from "@/auth/auth";

// Whether the signed-in person may act on machines. Administrators and operators may. False while nobody is
// signed in.
export function useCanDecide(): boolean {
  const user = useQuery(currentUserQuery).data ?? null;
  const roles = user?.roles ?? [];

  return roles.includes("Administrator") || roles.includes("Operator");
}
