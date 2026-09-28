// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { directoryQuery } from "@/users/users";

import type { LdapSettings } from "../signIn";

import { describeGroup } from "./groupDescription";

// The directory's names for the groups in the saved map, and for groups added from the search since then.
export function useDirectoryGroupNames(stored: LdapSettings | null) {
  // The server searches the directory with the saved settings, so only a saved server and search base can be searched.
  const storedReady =
    stored !== null &&
    stored.enabled &&
    (stored.host ?? "").trim() !== "" &&
    (stored.baseDn ?? "").trim() !== "";
  const directory = useQuery({
    ...directoryQuery,
    enabled: storedReady && Object.keys(stored.groupRoleMap).length > 0,
  });
  const [found, setFound] = useState<Map<string, string | null>>(new Map());
  const data = directory.data;
  const directoryNames = new Map<string, string | null>(
    data !== undefined && data.enabled && (data.host ?? "") !== "" && (data.baseDn ?? "") !== ""
      ? data.groupRoleMap.map((entry) => [entry.group.toLowerCase(), entry.name])
      : [],
  );

  return {
    storedReady,
    describe: (key: string) => describeGroup(key, found, directoryNames),
    remember: (group: string, name: string | null) => {
      setFound((current) => new Map(current).set(group.toLowerCase(), name));
    },
  };
}

export type DirectoryGroupNames = ReturnType<typeof useDirectoryGroupNames>;
