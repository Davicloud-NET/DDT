// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import { hasEntry } from "../signIn";

// The key typed to add to a role map. The server compares keys without regard to case, so a key the map has in any
// case is refused with the duplicate text; an added one gives Viewer.
export function useRoleMapAdd(
  map: Record<string, string>,
  onChange: (map: Record<string, string>) => void,
  duplicate: string,
) {
  const [typed, setTyped] = useState("");
  const [refused, setRefused] = useState<string | null>(null);

  return {
    typed,
    refused,
    type: (value: string) => {
      setTyped(value);
      setRefused(null);
    },
    add: () => {
      const key = typed.trim();

      if (key === "") {
        return;
      }

      if (hasEntry(map, key)) {
        setRefused(duplicate);
        return;
      }

      onChange({ ...map, [key]: "Viewer" });
      setTyped("");
      setRefused(null);
    },
  };
}

export type RoleMapAdd = ReturnType<typeof useRoleMapAdd>;
