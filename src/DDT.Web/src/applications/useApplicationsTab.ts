// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { useDeletion } from "@/library/useDeletion";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";
import { useListSearch } from "@/ui/useListSearch";

import { applicationEditOf, type ApplicationEdit } from "./applicationEdit";
import {
  applicationsQuery,
  deleteApplication,
  removeApplications,
  type ApplicationView,
} from "./applications";
import { matchesApplication } from "./applicationView";

// The drawer's application, or null for a new one, and what the form starts with. A new key opens a fresh form.
interface OpenDrawer {
  key: number;
  application: ApplicationView | null;
  seed: ApplicationEdit;
}

// The Applications tab's state: the live list, its search, and which dialog or drawer is open.
export function useApplicationsTab() {
  const queryClient = useQueryClient();
  const applications = useQuery({ ...applicationsQuery, ...liveListOptions(useLiveStatus()) });
  const canEdit = useIsAdministrator();
  const mark = useLiveMarks({
    queryKey: applicationsQuery.queryKey,
    items: (list) => list,
    id: (application) => application.id,
    signature: (application) =>
      `${String(application.revision)} ${String(application.usedBy.length)}`,
    tone: () => "idle",
  });
  const all = applications.data ?? [];
  const search = useListSearch(all, matchesApplication);
  const [adding, setAdding] = useState(false);
  const [drawer, setDrawer] = useState<OpenDrawer | null>(null);
  const deletion = useDeletion<ApplicationView>(deleteApplication, (id) => {
    removeApplications(queryClient, [id]);
  });

  const open = (application: ApplicationView | null, seed: ApplicationEdit) => {
    setAdding(false);
    setDrawer((current) => ({ key: (current?.key ?? 0) + 1, application, seed }));
  };

  return {
    applications,
    all,
    search,
    canEdit,
    mark,
    adding,
    drawer,
    deletion,
    startAdding: () => {
      setAdding(true);
    },
    stopAdding: () => {
      setAdding(false);
    },
    open,
    edit: (application: ApplicationView) => {
      open(application, applicationEditOf(application));
    },
    close: () => {
      setDrawer(null);
    },
  };
}
