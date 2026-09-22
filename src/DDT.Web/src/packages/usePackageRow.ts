// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";

import { equalJson } from "@/lib/equalJson";
import { useAutosave } from "@/lib/useAutosave";
import type { HardwareModel } from "@/machines/machines";

import { packagesQuery, updatePackage, type PackageSummary } from "./packages";

// What a package row changes. An empty description is saved as none.
export interface PackageEdit {
  name: string;
  description: string;
  targets: HardwareModel[];
}

function editOf(item: PackageSummary): PackageEdit {
  return { name: item.name, description: item.description ?? "", targets: item.targets };
}

// One package, saved as it is edited. Packages have no revision, so the last save wins, as on the server.
export function usePackageRow(item: PackageSummary) {
  const queryClient = useQueryClient();

  const autosave = useAutosave<PackageEdit, PackageSummary>({
    initial: { value: editOf(item), revision: 0 },
    save: (edit) =>
      updatePackage(item.id, {
        name: edit.name,
        description: edit.description.trim() === "" ? null : edit.description,
        targets: edit.targets,
      }),
    savedAs: (saved) => ({ value: editOf(saved), revision: 0 }),
    equals: equalJson,
    onSaved: (saved) => {
      queryClient.setQueryData(packagesQuery.queryKey, (list) =>
        list?.map((existing) => (existing.id === saved.id ? saved : existing)),
      );
    },
  });

  const { receive, update, state } = autosave;

  // Another administrator's change shows at once while this row has nothing unsaved.
  useEffect(() => {
    receive(editOf(item), 0);
  }, [receive, item]);

  return {
    edit: autosave.value,
    state,
    change: (patch: Partial<PackageEdit>, immediate = false) => {
      update((edit) => ({ ...edit, ...patch }), immediate);
    },
    // The server's refusal of one field, such as a target it cannot match by.
    messages: (field: keyof PackageEdit): string[] =>
      state.kind === "refused" ? (state.problem?.errors?.[field] ?? []) : [],
  };
}
