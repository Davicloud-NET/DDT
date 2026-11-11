// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { useDeletion } from "@/library/useDeletion";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";
import { useListSearch } from "@/ui/useListSearch";

import {
  applicationBundlesQuery,
  applicationsQuery,
  deleteBundle,
  removeBundles,
  type ApplicationBundleView,
} from "../applications";
import { matchesBundle } from "../applicationView";

// The Bundles tab's state: the live bundles and applications, the search, and which drawer is open.
export function useBundlesTab() {
  const queryClient = useQueryClient();
  const live = liveListOptions(useLiveStatus());
  const bundles = useQuery({ ...applicationBundlesQuery, ...live });
  const applications = useQuery({ ...applicationsQuery, ...live });
  const canEdit = useIsAdministrator();
  const mark = useLiveMarks({
    queryKey: applicationBundlesQuery.queryKey,
    items: (list) => list,
    id: (bundle) => bundle.id,
    signature: (bundle) => `${String(bundle.revision)} ${String(bundle.usedBy.length)}`,
    tone: () => "idle",
  });
  const library = applications.data;
  const nameOf = useCallback(
    (id: string) => library?.find((application) => application.id === id)?.name ?? null,
    [library],
  );
  const all = bundles.data ?? [];
  const search = useListSearch(all, (bundle, needle) => matchesBundle(bundle, needle, nameOf));
  // The drawer's bundle, or null for a new one. A new key opens a fresh form.
  const [drawer, setDrawer] = useState<{
    key: number;
    bundle: ApplicationBundleView | null;
  } | null>(null);
  const deletion = useDeletion<ApplicationBundleView>(deleteBundle, (id) => {
    removeBundles(queryClient, [id]);
  });

  return {
    bundles,
    all,
    library: library ?? [],
    nameOf,
    search,
    canEdit,
    mark,
    drawer,
    deletion,
    open: (bundle: ApplicationBundleView | null) => {
      setDrawer((current) => ({ key: (current?.key ?? 0) + 1, bundle }));
    },
    close: () => {
      setDrawer(null);
    },
  };
}
