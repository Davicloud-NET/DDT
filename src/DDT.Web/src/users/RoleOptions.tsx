// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { ListBoxItem } from "@/ui/Select";

import type { UserRole } from "./users";
import { roleDescription, roleLabel, ROLES } from "./userView";

// A role select's options, each with what the role may do.
export function RoleOptions({ roles = ROLES }: { roles?: readonly UserRole[] }) {
  return (
    <>
      {roles.map((role) => (
        <ListBoxItem
          key={role}
          id={role}
          textValue={roleLabel(role)}
          description={roleDescription(role)}
        >
          {roleLabel(role)}
        </ListBoxItem>
      ))}
    </>
  );
}
