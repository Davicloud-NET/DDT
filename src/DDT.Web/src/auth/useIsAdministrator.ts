// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";

import { currentUserQuery } from "./auth";

// Whether the signed-in person is an administrator; false while nobody is signed in.
export function useIsAdministrator(): boolean {
  const user = useQuery(currentUserQuery).data ?? null;

  return user?.roles.includes("Administrator") === true;
}
