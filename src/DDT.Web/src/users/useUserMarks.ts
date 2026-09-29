// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLiveMarks } from "@/live/useLiveMarks";

import { usersQuery } from "./users";

// A new account animates in. One whose role, sign-in or state changes flashes, such as after an action here.
export function useUserMarks(): (id: string) => string {
  return useLiveMarks({
    queryKey: usersQuery.queryKey,
    items: (list) => list,
    id: (user) => user.id,
    signature: (user) =>
      [
        user.role,
        user.disabled,
        user.lockedOutUntil,
        user.mustChangePassword,
        user.twoFactorEnabled,
      ].join("|"),
    tone: () => "idle",
  });
}
