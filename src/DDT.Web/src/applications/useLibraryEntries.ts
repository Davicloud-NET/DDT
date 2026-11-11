// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";

import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";

import { applicationBundlesQuery, applicationsQuery } from "./applications";
import { libraryEntries, type LibraryEntry } from "./libraryEntries";

// The library's bundles and applications, kept current by the hub.
export function useLibraryEntries(): LibraryEntry[] {
  const freshness = liveListOptions(useLiveStatus());
  const applications = useQuery({ ...applicationsQuery, ...freshness });
  const bundles = useQuery({ ...applicationBundlesQuery, ...freshness });

  return libraryEntries(applications.data ?? [], bundles.data ?? []);
}
