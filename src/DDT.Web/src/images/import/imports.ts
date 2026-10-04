// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiGet, apiPost } from "@/lib/api";
import type { ServerMessage } from "@/lib/serverText";

// A folder on the server that images are imported from. default marks the one in the store, which every server has.
export interface ImportFolder {
  path: string;
  default: boolean;
}

export interface ImportFile {
  path: string;
  name: string;
  sizeBytes: number;
  modifiedUtc: string;
}

// What became of one file or driver group. reason is why it was refused, and reasonText the same in English.
export interface ImportResult {
  name: string;
  outcome: "Added" | "Existing" | "Refused";
  reason: ServerMessage | null;
  reasonText: string | null;
}

// An import as the page follows it. item is what is being read, doneBytes of totalBytes how far that is.
export interface ImportStatus {
  startedUtc: string;
  startedBy: string;
  state: "Running" | "Finished";
  finishedUtc: string | null;
  item: string | null;
  doneBytes: number;
  totalBytes: number;
  items: number;
  results: ImportResult[];
}

// The files below the server's import folders, the MDT deployment shares among them, and the last import.
export interface ImportSources {
  folders: ImportFolder[];
  files: ImportFile[];
  shares: string[];
  job: ImportStatus | null;
}

export interface MdtImageFile {
  file: string;
  sizeBytes: number;
  found: boolean;
  names: string[];
}

// manufacturer and model are set when the group's path ends in a maker and a model.
export interface MdtDriverGroup {
  id: string;
  name: string;
  drivers: number;
  sizeBytes: number;
  manufacturer: string | null;
  model: string | null;
}

// What an MDT deployment share holds that DDT imports, and what it has no place for yet.
export interface MdtShareView {
  path: string;
  imageFiles: MdtImageFile[];
  driverGroups: MdtDriverGroup[];
  notImported: { applications: string[]; taskSequences: string[]; rules: string[] };
}

export const importSourcesQuery = queryOptions({
  queryKey: ["image-import"],
  queryFn: () => apiGet<ImportSources>("/api/images/import"),
});

export function mdtShareQuery(path: string) {
  return queryOptions({
    queryKey: ["image-import", "mdt", path],
    queryFn: () => apiPost<MdtShareView>("/api/images/import/mdt/inspect", { path }),
    staleTime: 0,
  });
}

// Both answer at once. The import follows in the status the hub pushes.
export function importFiles(paths: string[]): Promise<void> {
  return apiPost("/api/images/import", { paths });
}

export function importShare(
  path: string,
  imageFiles: string[],
  driverGroups: string[],
): Promise<void> {
  return apiPost("/api/images/import/mdt", { path, imageFiles, driverGroups });
}

// The hub's status goes into the sources the page holds. The images and packages an import adds arrive by themselves.
export function putImportStatus(queryClient: QueryClient, status: ImportStatus): void {
  queryClient.setQueryData(importSourcesQuery.queryKey, (sources) =>
    sources === undefined ? sources : { ...sources, job: status },
  );
}
